using System;
using System.Diagnostics;
using System.Globalization;
using System.IO;
using System.Runtime.InteropServices;
using System.Text;
using System.Threading;

namespace CodexStrip {
 // This code runs in the disposable --state-reader process, never on the UI thread.
 public sealed class StateSnapshotReadResult {
  public byte[] Bytes;
  public int Status;
  public int Win32Error;
  public long FileWriteUtcTicks;
 }

 // A pending kernel request still owns its native storage. The helper must exit;
 // it must not catch this exception and resume its request loop.
 public sealed class StateSnapshotFatalException : Exception {
  public StateSnapshotFatalException() : base("State reader native completion failed.") { }
 }

 public static class StateSnapshotHelper {
  const int Magic = 0x53535231;
  static int ReadCommand(Stream input) {
   byte[] command = new byte[16]; int length = 0;
   for (;;) {
    int next = input.ReadByte();
    if (next < 0) return 0;
    if (next == 10) break;
    if (next == 13) continue;
    if (length == command.Length) return -1;
    command[length++] = (byte)next;
   }
   string value = Encoding.ASCII.GetString(command, 0, length);
   if (value == "READ") return 1;
   if (value == "EXIT") return 0;
   return -1;
  }
  public static int Run(string[] args) {
   Process parent = null;
   try {
    if (args == null || args.Length != 4 || args[0] != "--state-reader" || args[1].Length > 131072) return 64;
    string path = new UTF8Encoding(false, true).GetString(Convert.FromBase64String(args[1]));
    path = CooperativeStateFile.ValidateBackupPath(path);
    int parentPid; long parentStart;
    if (!Int32.TryParse(args[2], NumberStyles.None, CultureInfo.InvariantCulture, out parentPid) || parentPid <= 0 ||
        !Int64.TryParse(args[3], NumberStyles.None, CultureInfo.InvariantCulture, out parentStart) || parentStart <= 0 ||
        parentPid == Process.GetCurrentProcess().Id) return 64;
    parent = Process.GetProcessById(parentPid);
    if (parent.StartTime.ToUniversalTime().Ticks != parentStart || parent.HasExited) return 65;
    // Holding this Process handle and checking its creation time avoids PID reuse.
    Process observedParent = parent;
    Thread parentWatch = new Thread(delegate() {
     try { observedParent.WaitForExit(); }
     catch { }
     // Termination also releases a file if native I/O or the command loop is stuck.
     Environment.Exit(0);
    });
    parentWatch.IsBackground = true; parentWatch.Name = "state-reader-parent"; parentWatch.Start();
    using (Stream input = Console.OpenStandardInput())
    using (BinaryWriter output = new BinaryWriter(Console.OpenStandardOutput())) {
     for (;;) {
      int command = ReadCommand(input);
      if (command == 0) return 0;
      if (command < 0) return 64;
      StateSnapshotReadResult result = CooperativeStateFile.Read(path, 250);
      int length = result.Status == 0 && result.Bytes != null ? result.Bytes.Length : 0;
      output.Write(Magic); output.Write(result.Status); output.Write(result.Win32Error);
      output.Write(length); output.Write(result.FileWriteUtcTicks);
      if (length > 0) output.Write(result.Bytes);
      output.Flush();
     }
    }
   } catch (StateSnapshotFatalException) {
    try { Console.Error.WriteLine("State reader native completion failed."); } catch { }
    return 70;
   } catch (Exception) {
    // No paths, state contents, or exception strings are written to stderr.
    try { Console.Error.WriteLine("State reader stopped."); } catch { }
    return 71;
   }
   // The process exits immediately after Run. The background parent waiter is
   // intentionally not joined, and no worker outlives this helper process.
  }
 }

 public static class CooperativeStateFile {
  public const int MaxSnapshotBytes = 64 * 1024 * 1024;
  static int nativeFailed;
  internal static string ValidateBackupPath(string path) {
   if (String.IsNullOrEmpty(path) || path.IndexOf('\0') >= 0 || !Path.IsPathRooted(path)) throw new ArgumentException("Backup path required.");
   // Reject drive-relative paths and Win32 device names. Normal local and UNC
   // fully qualified file paths are supported; no primary .json path is accepted.
   bool drive = path.Length >= 3 && Char.IsLetter(path[0]) && path[1] == ':' && (path[2] == '\\' || path[2] == '/');
   bool unc = path.StartsWith(@"\\", StringComparison.Ordinal) && !path.StartsWith(@"\\?\", StringComparison.Ordinal) && !path.StartsWith(@"\\.\", StringComparison.Ordinal);
   if (!drive && !unc) throw new ArgumentException("Absolute backup path required.");
   string full = Path.GetFullPath(path);
   if (!full.EndsWith(".bak", StringComparison.OrdinalIgnoreCase) || full.Length > 32760 || full.IndexOf(':', drive ? 2 : 0) >= 0) throw new ArgumentException("Backup path required.");
   return full;
  }
  static StateSnapshotReadResult Failure(int status, int error) {
   return new StateSnapshotReadResult { Status = status, Win32Error = error };
  }
  internal static void Fatal() {
   Interlocked.Exchange(ref nativeFailed, 1);
   throw new StateSnapshotFatalException();
  }
  public static StateSnapshotReadResult Read(string path, int maximumLeaseMs = 250) {
   if (Interlocked.CompareExchange(ref nativeFailed, 0, 0) != 0) throw new StateSnapshotFatalException();
   string full;
   try { full = ValidateBackupPath(path); } catch { return Failure(4, 87); }
   // A caller cannot accidentally remove or expand the bounded lease.
   if (maximumLeaseMs < 1 || maximumLeaseMs > 250) return Failure(4, 87);
   NativeLease lease = null;
   try {
    lease = new NativeLease();
    int error;
    if (!lease.Acquire(full, maximumLeaseMs, out error)) {
     if (error == 2 || error == 3) return Failure(1, error);
     if (error == 5 || error == 32 || error == 33 || error == 300 || error == 301 || error == 803) return Failure(2, error);
     return Failure(4, error);
    }
    return lease.ReadBytes();
   } catch (StateSnapshotFatalException) { throw; }
   catch (OutOfMemoryException) { return Failure(4, 8); }
   catch { return Failure(4, 31); }
   finally { if (lease != null) lease.Dispose(); }
  }

  sealed class NativeLease : IDisposable {
   const uint FsctlRequestOplock = 0x00090240;
   const uint WaitObject = 0, WaitTimeout = 258;
   const int IoPending = 997, OperationAborted = 995, ChunkBytes = 65536, CleanupMs = 500;
   readonly object gate = new object();
   IntPtr file, opEvent, readEvent, input, output, opOverlapped, readOverlapped, readBuffer, createIo;
   Thread monitor; Timer timer;
   bool opPending, readPending, disposed, nativeRetained, monitorStarted;
   volatile bool stopped, interrupted;

   [StructLayout(LayoutKind.Sequential)] struct UnicodeString { public ushort Length, MaximumLength; public IntPtr Buffer; }
   [StructLayout(LayoutKind.Sequential)] struct ObjectAttributes {
    public int Length; public IntPtr RootDirectory, ObjectName; public uint Attributes; public IntPtr SecurityDescriptor, SecurityQualityOfService;
   }
   [StructLayout(LayoutKind.Sequential)] struct Overlapped {
    public IntPtr Internal, InternalHigh; public uint Offset, OffsetHigh; public IntPtr Event;
   }
   [StructLayout(LayoutKind.Sequential)] struct FileInformation {
    public uint Attributes, CreationLow, CreationHigh, AccessLow, AccessHigh, WriteLow, WriteHigh;
    public uint VolumeSerial, SizeHigh, SizeLow, LinkCount, IndexHigh, IndexLow;
   }
   [DllImport("ntdll.dll")] static extern int NtCreateFile(out IntPtr file, uint access, ref ObjectAttributes attributes, IntPtr ioStatus, IntPtr allocation, uint fileAttributes, uint share, uint disposition, uint options, IntPtr ea, uint eaLength);
   [DllImport("ntdll.dll")] static extern uint RtlNtStatusToDosError(int status);
   [DllImport("kernel32.dll", SetLastError = true)] static extern bool DeviceIoControl(IntPtr file, uint control, IntPtr input, uint inputLength, IntPtr output, uint outputLength, IntPtr returned, IntPtr overlapped);
   [DllImport("kernel32.dll", CharSet = CharSet.Unicode, SetLastError = true)] static extern IntPtr CreateEventW(IntPtr attributes, bool manual, bool initial, string name);
   [DllImport("kernel32.dll", SetLastError = true)] static extern bool CloseHandle(IntPtr handle);
   [DllImport("kernel32.dll", SetLastError = true)] static extern uint WaitForSingleObject(IntPtr handle, uint milliseconds);
   [DllImport("kernel32.dll", SetLastError = true)] static extern bool ReadFile(IntPtr file, IntPtr data, uint length, IntPtr returned, IntPtr overlapped);
   [DllImport("kernel32.dll", SetLastError = true)] static extern bool GetOverlappedResult(IntPtr file, IntPtr overlapped, out uint transferred, bool wait);
   [DllImport("kernel32.dll", SetLastError = true)] static extern bool ResetEvent(IntPtr handle);
   [DllImport("kernel32.dll", SetLastError = true)] static extern bool GetFileInformationByHandle(IntPtr file, out FileInformation information);

   static IntPtr Allocate(int bytes) {
    IntPtr address = Marshal.AllocHGlobal(bytes);
    for (int i = 0; i < bytes; i++) Marshal.WriteByte(address, i, 0);
    return address;
   }
   public NativeLease() {
    try {
     // Allocate everything before the atomic open so no allocations or file I/O
     // separate NtCreateFile from FSCTL_REQUEST_OPLOCK.
     input = Allocate(12); output = Allocate(24);
     opOverlapped = Allocate(Marshal.SizeOf(typeof(Overlapped)));
     readOverlapped = Allocate(Marshal.SizeOf(typeof(Overlapped)));
     readBuffer = Marshal.AllocHGlobal(ChunkBytes); createIo = Allocate(IntPtr.Size * 2);
     opEvent = CreateEventW(IntPtr.Zero, true, false, null);
     if (opEvent == IntPtr.Zero) throw new IOException("Native event unavailable.");
     readEvent = CreateEventW(IntPtr.Zero, true, false, null);
     if (readEvent == IntPtr.Zero) throw new IOException("Native event unavailable.");
     Marshal.WriteInt16(input, 0, 1); Marshal.WriteInt16(input, 2, 12);
     Marshal.WriteInt32(input, 4, 3); Marshal.WriteInt32(input, 8, 1); // RH / request.
     Marshal.StructureToPtr(new Overlapped { Event = opEvent }, opOverlapped, false);
     timer = new Timer(Expire, null, Timeout.Infinite, Timeout.Infinite);
    } catch { Dispose(); throw; }
   }
   public bool Acquire(string path, int maximumLeaseMs, out int error) {
    error = 0;
    string ntPath = path.StartsWith(@"\\", StringComparison.Ordinal) ? @"\??\UNC\" + path.Substring(2) : @"\??\" + path;
    IntPtr name = IntPtr.Zero, unicodeAddress = IntPtr.Zero;
    try {
     name = Marshal.StringToHGlobalUni(ntPath);
     UnicodeString unicode = new UnicodeString { Length = checked((ushort)(ntPath.Length * 2)), MaximumLength = checked((ushort)(ntPath.Length * 2 + 2)), Buffer = name };
     unicodeAddress = Marshal.AllocHGlobal(Marshal.SizeOf(typeof(UnicodeString)));
     Marshal.StructureToPtr(unicode, unicodeAddress, false);
     ObjectAttributes attributes = new ObjectAttributes { Length = Marshal.SizeOf(typeof(ObjectAttributes)), ObjectName = unicodeAddress, Attributes = 0x40 };
     lock (gate) {
      // FILE_OPEN_REQUIRING_OPLOCK | FILE_NON_DIRECTORY_FILE, sharing READ|DELETE.
      int status = NtCreateFile(out file, 0x80000000, ref attributes, createIo, IntPtr.Zero, 0x80, 5, 1, 0x10040, IntPtr.Zero, 0);
      if (status == 0x103) {
       // Unexpected asynchronous create: keep its IOSB and name storage alive
       // until process termination rather than risk freeing kernel-owned memory.
       nativeRetained = true; name = IntPtr.Zero; unicodeAddress = IntPtr.Zero;
       CloseFileLocked(); Fatal();
      }
      if (status != 0) { error = unchecked((int)RtlNtStatusToDosError(status)); CloseFileLocked(); return false; }
      bool immediate = DeviceIoControl(file, FsctlRequestOplock, input, 12, output, 24, IntPtr.Zero, opOverlapped);
      error = immediate ? 0 : Marshal.GetLastWin32Error();
      if (immediate || error != IoPending) { CloseFileLocked(); if (error == 0) error = 300; return false; }
      opPending = true; error = 0;
      timer.Change(maximumLeaseMs, Timeout.Infinite);
     }
     monitor = new Thread(WatchBreak); monitor.IsBackground = true; monitor.Name = "state-reader-yield"; monitor.Start(); monitorStarted = true;
     return true;
    } finally {
     if (unicodeAddress != IntPtr.Zero) Marshal.FreeHGlobal(unicodeAddress);
     if (name != IntPtr.Zero) Marshal.FreeHGlobal(name);
    }
   }
   void Expire(object state) {
    lock (gate) {
     if (stopped || file == IntPtr.Zero) return;
     interrupted = true; CloseFileLocked();
    }
   }
   void WatchBreak() {
    uint result = WaitForSingleObject(opEvent, UInt32.MaxValue);
    lock (gate) {
     if (stopped) return;
     interrupted = true;
     // A close is the implicit break acknowledgement; never wait for parsing.
     CloseFileLocked();
     if (result != WaitObject) nativeRetained = true;
    }
   }
   void CloseFileLocked() {
    if (file == IntPtr.Zero || file == new IntPtr(-1)) { file = IntPtr.Zero; return; }
    IntPtr closing = file; file = IntPtr.Zero;
    // Closing cancels outstanding I/O and acknowledges a break atomically from
    // the other process's perspective. Do not cancel the oplock first: that
    // would create an unprotected interval with this file handle still open.
    if (!CloseHandle(closing)) nativeRetained = true;
   }
   StateSnapshotReadResult InterruptedResult(int error) { return Failure(3, error); }
   public StateSnapshotReadResult ReadBytes() {
    FileInformation info; int error;
    lock (gate) {
     if (interrupted || file == IntPtr.Zero) return InterruptedResult(OperationAborted);
     if (!GetFileInformationByHandle(file, out info)) { error = Marshal.GetLastWin32Error(); return Failure(4, error); }
    }
    ulong size = ((ulong)info.SizeHigh << 32) | info.SizeLow;
    if (size > MaxSnapshotBytes) return Failure(4, 223); // ERROR_FILE_TOO_LARGE.
    long fileTime = unchecked((long)(((ulong)info.WriteHigh << 32) | info.WriteLow));
    long ticks;
    try { ticks = DateTime.FromFileTimeUtc(fileTime).Ticks; } catch { return Failure(4, 13); }
    byte[] bytes = new byte[(int)size]; int total = 0;
    while (total < bytes.Length) {
     bool immediate;
     lock (gate) {
      if (interrupted || file == IntPtr.Zero) return InterruptedResult(OperationAborted);
      if (!ResetEvent(readEvent)) return Failure(4, Marshal.GetLastWin32Error());
      Marshal.StructureToPtr(new Overlapped { Offset = (uint)total, Event = readEvent }, readOverlapped, false);
      immediate = ReadFile(file, readBuffer, (uint)Math.Min(ChunkBytes, bytes.Length - total), IntPtr.Zero, readOverlapped);
      error = immediate ? 0 : Marshal.GetLastWin32Error();
      readPending = !immediate && error == IoPending;
     }
     if (!immediate && error != IoPending) return interrupted ? InterruptedResult(error) : Failure(4, error);
     if (readPending) {
      uint wait = WaitForSingleObject(readEvent, 250);
      if (wait != WaitObject) {
       lock (gate) { interrupted = true; CloseFileLocked(); }
       if (WaitForSingleObject(readEvent, CleanupMs) != WaitObject) { nativeRetained = true; Fatal(); }
      }
      readPending = false;
     }
     uint count;
     lock (gate) {
      if (interrupted || file == IntPtr.Zero) return InterruptedResult(OperationAborted);
      if (!GetOverlappedResult(file, readOverlapped, out count, false)) {
       error = Marshal.GetLastWin32Error();
       // An unexpected still-pending result must be cancelled before disposal.
       if (error == 996) readPending = true;
       return error == OperationAborted ? InterruptedResult(error) : Failure(4, error);
      }
     }
     if (count == 0 || count > bytes.Length - total) return Failure(4, 38);
     Marshal.Copy(readBuffer, bytes, total, (int)count); total += (int)count;
    }
    lock (gate) {
     // A break can be signalled before the watcher receives CPU time. Discard
     // that snapshot too, and close the file before returning its bytes.
     if (interrupted || file == IntPtr.Zero || WaitForSingleObject(opEvent, 0) != WaitTimeout) interrupted = true;
     stopped = true; CloseFileLocked();
     if (interrupted) return InterruptedResult(OperationAborted);
    }
    return new StateSnapshotReadResult { Bytes = bytes, Status = 0, FileWriteUtcTicks = ticks };
   }
   static uint Remaining(Stopwatch clock) {
    long left = CleanupMs - clock.ElapsedMilliseconds;
    return left > 0 ? (uint)left : 0;
   }
   public void Dispose() {
    lock (gate) {
     if (disposed) return;
     disposed = true; stopped = true;
     if (timer != null) { timer.Dispose(); timer = null; }
     CloseFileLocked();
    }
    if (nativeRetained) Fatal();
    Stopwatch deadline = Stopwatch.StartNew();
    if (readPending && WaitForSingleObject(readEvent, Remaining(deadline)) != WaitObject) { nativeRetained = true; Fatal(); }
    readPending = false;
    if (opPending && WaitForSingleObject(opEvent, Remaining(deadline)) != WaitObject) { nativeRetained = true; Fatal(); }
    if (monitorStarted && monitor != Thread.CurrentThread && !monitor.Join((int)Remaining(deadline))) { nativeRetained = true; Fatal(); }
    // All kernel requests and the event waiter are now finished. Only here is
    // it safe to release OVERLAPPED records, native buffers, and event handles.
    if (opEvent != IntPtr.Zero) CloseHandle(opEvent);
    if (readEvent != IntPtr.Zero) CloseHandle(readEvent);
    if (input != IntPtr.Zero) Marshal.FreeHGlobal(input);
    if (output != IntPtr.Zero) Marshal.FreeHGlobal(output);
    if (opOverlapped != IntPtr.Zero) Marshal.FreeHGlobal(opOverlapped);
    if (readOverlapped != IntPtr.Zero) Marshal.FreeHGlobal(readOverlapped);
    if (readBuffer != IntPtr.Zero) Marshal.FreeHGlobal(readBuffer);
    if (createIo != IntPtr.Zero) Marshal.FreeHGlobal(createIo);
   }
  }
 }
}
