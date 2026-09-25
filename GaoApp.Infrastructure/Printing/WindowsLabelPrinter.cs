using System.ComponentModel;
using System.Runtime.InteropServices;
using System.Runtime.Versioning;

namespace GaoApp.Infrastructure.Printing;

public interface ILabelPrintTransport
{
    int Send(string printerName, string documentName, IEnumerable<byte[]> commands);
}

[SupportedOSPlatform("windows")]
public sealed class WindowsLabelPrinter : ILabelPrintTransport
{
    [StructLayout(LayoutKind.Sequential, CharSet = CharSet.Unicode)]
    private struct PrinterInfo4 { public string PrinterName; public string ServerName; public uint Attributes; }
    [StructLayout(LayoutKind.Sequential, CharSet = CharSet.Unicode)]
    private struct DocInfo { public string DocName; public string? OutputFile; public string DataType; }
    [DllImport("winspool.drv", CharSet = CharSet.Unicode, SetLastError = true)]
    private static extern bool EnumPrinters(uint flags, string? name, uint level, IntPtr buffer, uint size, out uint needed, out uint returned);
    [DllImport("winspool.drv", CharSet = CharSet.Unicode, SetLastError = true)]
    private static extern bool OpenPrinter(string name, out IntPtr handle, IntPtr defaults);
    [DllImport("winspool.drv", SetLastError = true)] private static extern bool ClosePrinter(IntPtr handle);
    [DllImport("winspool.drv", CharSet = CharSet.Unicode, SetLastError = true)]
    private static extern int StartDocPrinter(IntPtr handle, int level, ref DocInfo info);
    [DllImport("winspool.drv", SetLastError = true)] private static extern bool EndDocPrinter(IntPtr handle);
    [DllImport("winspool.drv", SetLastError = true)] private static extern bool StartPagePrinter(IntPtr handle);
    [DllImport("winspool.drv", SetLastError = true)] private static extern bool EndPagePrinter(IntPtr handle);
    [DllImport("winspool.drv", SetLastError = true)] private static extern bool AbortPrinter(IntPtr handle);
    [DllImport("winspool.drv", SetLastError = true)] private static extern bool WritePrinter(IntPtr handle, IntPtr bytes, int count, out int written);

    public static IReadOnlyList<string> Installed()
    {
        EnumPrinters(6, null, 4, IntPtr.Zero, 0, out uint needed, out _);
        if (needed == 0) return [];
        var pointer = Marshal.AllocHGlobal(checked((int)needed));
        try
        {
            if (!EnumPrinters(6, null, 4, pointer, needed, out _, out uint count)) throw new Win32Exception(Marshal.GetLastWin32Error());
            var result = new List<string>(); int size = Marshal.SizeOf<PrinterInfo4>();
            for (int i = 0; i < count; i++) result.Add(Marshal.PtrToStructure<PrinterInfo4>(pointer + size * i).PrinterName);
            return result.OrderBy(x => x).ToArray();
        }
        finally { Marshal.FreeHGlobal(pointer); }
    }

    public int Send(string printerName, string documentName, IEnumerable<byte[]> commands)
    {
        if (!Installed().Contains(printerName, StringComparer.OrdinalIgnoreCase)) throw new InvalidOperationException("Tài khoản dịch vụ không thấy máy in Windows đã chọn.");
        if (!OpenPrinter(printerName, out var handle, IntPtr.Zero)) throw new Win32Exception(Marshal.GetLastWin32Error());
        bool started = false;
        try
        {
            var info = new DocInfo { DocName = documentName, DataType = "RAW" };
            int jobId = StartDocPrinter(handle, 1, ref info);
            if (jobId == 0) throw new Win32Exception(Marshal.GetLastWin32Error());
            started = true;
            if (!StartPagePrinter(handle)) throw new Win32Exception(Marshal.GetLastWin32Error());
            foreach (var bytes in commands)
            {
                var pin = GCHandle.Alloc(bytes, GCHandleType.Pinned);
                try
                {
                    int offset = 0;
                    while (offset < bytes.Length)
                    {
                        if (!WritePrinter(handle, pin.AddrOfPinnedObject() + offset, bytes.Length - offset, out int written) || written <= 0)
                            throw new Win32Exception(Marshal.GetLastWin32Error());
                        offset += written;
                    }
                }
                finally { pin.Free(); }
            }
            if (!EndPagePrinter(handle)) throw new Win32Exception(Marshal.GetLastWin32Error());
            if (!EndDocPrinter(handle)) throw new Win32Exception(Marshal.GetLastWin32Error());
            started = false; return jobId;
        }
        finally { if (started) AbortPrinter(handle); ClosePrinter(handle); }
    }
}
