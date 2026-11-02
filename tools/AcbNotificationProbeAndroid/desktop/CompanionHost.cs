using System;
using System.Collections.Generic;
using System.Drawing;
using System.IO;
using System.Runtime.InteropServices;
using System.Security.Cryptography;
using System.Text;
using System.Text.RegularExpressions;
using System.Threading;
using System.Windows.Forms;

namespace GaoAppCompanion {
    static class Program {
        [STAThread] static int Main(string[] args) {
            try {
                if (args.Length > 0 && args[0] == "--self-test") { SelfTest(); return 0; }
                if (args.Length > 0 && args[0] == "--smoke") {
                    using (var host = new BluetoothHost(delegate(string message) { Console.WriteLine(message); })) {
                        host.Start(); Console.WriteLine("PASS: real radio, authenticated/encrypted RFCOMM listener and SDP service registered; channel " + host.Channel); Thread.Sleep(1000);
                    }
                    Console.WriteLine("PASS: service removed and radio discovery restored"); return 0;
                }
                Application.EnableVisualStyles(); Application.SetCompatibleTextRenderingDefault(false);
                using (var form = new HostForm()) {
                    if (args.Length == 2 && args[0] == "--render") { form.StartPosition=FormStartPosition.Manual; form.Location=new Point(-30000,-30000); form.Show(); form.PerformLayout(); Application.DoEvents(); using (var bitmap = new Bitmap(form.Width,form.Height)) { form.DrawToBitmap(bitmap,new Rectangle(0,0,bitmap.Width,bitmap.Height)); bitmap.Save(args[1]); } form.Close(); return 0; }
                    Application.Run(form);
                }
                return 0;
            } catch (Exception error) { Console.Error.WriteLine(error.Message); return 1; }
        }
        static void Require(bool value, string name) { if (!value) throw new InvalidOperationException(name); }
        static void SelfTest() {
            Require(Marshal.SizeOf(typeof(Native.BthAddress)) == 30,"SOCKADDR_BTH must be packed to 30 bytes");
            Require(Marshal.SizeOf(typeof(Native.QuerySet)) == (IntPtr.Size == 8 ? 120 : 60),"WSAQUERYSET layout");
            Require(Marshal.SizeOf(typeof(Native.RadioInfo)) == 520,"BLUETOOTH_RADIO_INFO layout");
            var nonce = new string('a',32); string pin = "12345678";
            Require(Protocol.Reply("HELLO GAOAPP1 " + pin + " " + nonce,pin,false) == "OK GAOAPP1 " + nonce,"valid handshake");
            foreach (string value in new [] { "HELLO GAOAPP1 87654321 " + nonce, "HELLO GAOAPP1 " + pin + " abc", "PING GAOAPP1", "HELLO GAOAPP1 " + pin + " " + nonce + " EXTRA", "+8000 VND POS-123" }) Require(Protocol.Reply(value,pin,false) == null,"reject malformed or untrusted message");
            Require(Protocol.Reply("PING GAOAPP1",pin,true) == "PONG GAOAPP1","heartbeat");
            Require(Protocol.Reply("NOTIFICATION secret",pin,true) == null,"notification content not supported");
            Console.WriteLine("PASS: 11 protocol and native-layout checks");
        }
    }
    static class Protocol {
        internal static readonly Guid ServiceId = new Guid("9102c46c-e477-4e82-9dba-21fba321ec70");
        internal static string Reply(string line,string pin,bool authenticated) {
            if (authenticated) return line == "PING GAOAPP1" ? "PONG GAOAPP1" : null;
            if (!Regex.IsMatch(pin ?? "", "^[0-9]{8}$")) return null;
            var match = Regex.Match(line ?? "", "^HELLO GAOAPP1 ([0-9]{8}) ([a-f0-9]{32})$");
            return match.Success && match.Groups[1].Value == pin ? "OK GAOAPP1 " + match.Groups[2].Value : null;
        }
    }
    sealed class HostForm : Form {
        readonly TextBox pairing = new TextBox { ReadOnly=true, Font=new Font("Consolas",15), Width=565 };
        readonly Label info = new Label { AutoSize=false, Width=565, Height=65, Text="Bấm Bắt đầu để mở kết nối với điện thoại.", Font=new Font("Segoe UI",11) };
        readonly Label status = new Label { AutoSize=false, Width=565, Height=90, Text="Chưa mở kết nối.", Font=new Font("Segoe UI",11) };
        readonly Button start = new Button { Text="Bắt đầu", Width=140, Height=42 };
        readonly Button stop = new Button { Text="Dừng", Width=140, Height=42 };
        BluetoothHost host;
        public HostForm() {
            Text="GaoApp — Máy tính đồng hành"; Width=630; Height=540; StartPosition=FormStartPosition.CenterScreen; Font=new Font("Segoe UI",11); BackColor=Color.FromArgb(243,246,245);
            var page = new FlowLayoutPanel { Dock=DockStyle.Fill, FlowDirection=FlowDirection.TopDown, WrapContents=false, Padding=new Padding(22), AutoScroll=true };
            page.Controls.Add(new Label { Text="Thử thiết bị đồng hành ACB", Font=new Font("Segoe UI",19,FontStyle.Bold), Width=565, Height=48 });
            page.Controls.Add(info); page.Controls.Add(pairing);
            page.Controls.Add(new Label { Text="Trên điện thoại: GaoApp ACB Probe → Ghép máy tính đồng hành. Nhập đầy đủ mã trên, chọn đúng máy tính và xác nhận ghép nối trên cả hai thiết bị.", Width=565, Height=88 });
            var actions = new FlowLayoutPanel { Width=565,Height=52 }; actions.Controls.Add(start); actions.Controls.Add(stop); page.Controls.Add(actions); page.Controls.Add(status);
            page.Controls.Add(new Label { Text="Chỉ trao đổi tín hiệu kiểm tra kết nối. Máy tính không nhận nội dung ngân hàng. Sau khi ghép, thử thông báo mới và xuất báo cáo trên điện thoại.", Width=565, Height=74, ForeColor=Color.FromArgb(88,110,107) }); Controls.Add(page);
            start.Click += delegate { StartHost(); }; stop.Click += delegate { StopHost(); }; FormClosed += delegate { StopHost(); };
        }
        void StartHost() {
            if (host != null) return;
            try {
                host=new BluetoothHost(delegate(string text) { if (!IsDisposed && IsHandleCreated) BeginInvoke((Action)delegate { status.Text=text; }); });
                host.Start(); info.Text="Bluetooth máy tính: " + host.RadioName + "\nCó 10 phút để ghép thiết bị mới. Mã có hiệu lực trong phiên này."; pairing.Text=host.PairingCode;
                start.Enabled=false; status.Text="Sẵn sàng nhận điện thoại. Nếu Windows hỏi ghép Bluetooth, hãy xác nhận đúng OPPO của bạn.";
            } catch (Exception error) { StopHost(); status.Text="Chưa mở được Bluetooth: " + error.Message; }
        }
        void StopHost() { if (host!=null) { host.Dispose(); host=null; } pairing.Text=""; start.Enabled=true; status.Text="Đã dừng; thông tin tìm thấy thiết bị được khôi phục."; }
    }
    sealed class BluetoothHost : IDisposable {
        IntPtr radio=IntPtr.Zero, listener=new IntPtr(-1), client=new IntPtr(-1);
        bool winsock, registered, wasDiscoverable, wasConnectable, radioStateKnown;
        volatile bool stopped;
        readonly Action<string> notify;
        readonly List<IntPtr> allocations=new List<IntPtr>();
        Native.QuerySet query;
        public string RadioName { get; private set; }
        public string PairingCode { get; private set; }
        public uint Channel { get; private set; }
        string pin;
        public BluetoothHost(Action<string> status) { notify=status; }
        IntPtr Allocate(object value) { var ptr=Marshal.AllocHGlobal(Marshal.SizeOf(value)); allocations.Add(ptr); Marshal.StructureToPtr(value,ptr,false); return ptr; }
        IntPtr StringPointer(string value) { var ptr=Marshal.StringToHGlobalUni(value); allocations.Add(ptr); return ptr; }
        static void Check(int result,string action) { if (result == -1) throw new IOException(action + " (Windows " + Native.WSAGetLastError() + ")"); }
        public void Start() {
            var parameters=new Native.FindRadioParams { Size=4 }; IntPtr found=Native.BluetoothFindFirstRadio(ref parameters,out radio);
            if (found==IntPtr.Zero) throw new IOException("Không tìm thấy radio Bluetooth đang bật.");
            Native.BluetoothFindRadioClose(found);
            var info=new Native.RadioInfo { Size=520 };
            int result=Native.BluetoothGetRadioInfo(radio,ref info); if (result!=0) throw new IOException("Không đọc được Bluetooth (Windows " + result + ").");
            RadioName=info.Name; wasDiscoverable=Native.BluetoothIsDiscoverable(radio); wasConnectable=Native.BluetoothIsConnectable(radio); radioStateKnown=true;
            IntPtr startup=Marshal.AllocHGlobal(512);
            try { result=Native.WSAStartup(0x202,startup); if (result!=0) throw new IOException("WSAStartup " + result); winsock=true; } finally { Marshal.FreeHGlobal(startup); }
            listener=Native.socket(32,1,3); if (listener==new IntPtr(-1)) throw new IOException("Không mở được RFCOMM: " + Native.WSAGetLastError());
            int enabled=1; Check(Native.setsockopt(listener,3,0x80000001,ref enabled,4),"Bluetooth authentication"); Check(Native.setsockopt(listener,3,0x00000002,ref enabled,4),"Bluetooth encryption");
            var address=new Native.BthAddress { Family=32,Address=info.Address,Port=0xFFFFFFFF };
            Check(Native.bind(listener,ref address,30),"RFCOMM bind"); int length=30; Check(Native.getsockname(listener,ref address,ref length),"RFCOMM channel"); Channel=address.Port;
            Check(Native.listen(listener,1),"RFCOMM listen");
            var endpoints=new Native.CsAddress { Local=new Native.SocketAddress { Address=Allocate(address),Length=30 },SocketType=1,Protocol=3 };
            query=new Native.QuerySet { Size=Marshal.SizeOf(typeof(Native.QuerySet)),Namespace=16,Name=StringPointer("GaoApp Companion Probe"),Comment=StringPointer("GaoApp local health-check companion; no financial data"),ServiceClass=Allocate(Protocol.ServiceId),AddressCount=1,Addresses=Allocate(endpoints) };
            Check(Native.WSASetService(ref query,0,0),"Bluetooth SDP"); registered=true;
            if (!wasConnectable && !Native.BluetoothEnableIncomingConnections(radio,true)) throw new IOException("Chưa cho phép kết nối Bluetooth.");
            if (!wasDiscoverable && !Native.BluetoothEnableDiscovery(radio,true)) throw new IOException("Chưa cho phép tìm thấy máy tính Bluetooth.");
            byte[] random=new byte[4]; using (var rng=RandomNumberGenerator.Create()) rng.GetBytes(random); pin=(BitConverter.ToUInt32(random,0)%100000000).ToString("D8");
            string hex=info.Address.ToString("X12"); string mac=""; for (int i=0;i<6;i++) mac+=(i==0 ? "" : ":")+hex.Substring(i*2,2);
            PairingCode="GAO1|" + mac + "|" + pin;
            new Thread(AcceptLoop) { IsBackground=true }.Start();
            // Limit new-device discoverability to ten minutes; an existing paired connection remains valid.
            new Thread(new ThreadStart(delegate { for (int i=0;i<600 && !stopped;i++) Thread.Sleep(1000); lock(this) { if (!stopped && radio!=IntPtr.Zero && !wasDiscoverable) { Native.BluetoothEnableDiscovery(radio,false); notify("Hết thời gian tìm thiết bị mới. Kết nối đã ghép vẫn dùng được; Dừng/Bắt đầu để ghép lại."); } } })) { IsBackground=true }.Start();
        }
        void AcceptLoop() {
            while (!stopped) {
                try {
                    IntPtr accepted=Native.accept(listener,IntPtr.Zero,IntPtr.Zero);
                    if (accepted==new IntPtr(-1)) { if (stopped) return; Thread.Sleep(1000); continue; }
                    if (stopped) { Native.closesocket(accepted); return; }
                    client=accepted;
                    int timeout=20000; Check(Native.setsockopt(client,65535,0x1006,ref timeout,4),"Receive timeout");
                    timeout=10000; Check(Native.setsockopt(client,65535,0x1005,ref timeout,4),"Send timeout");
                    bool authenticated=false;
                    for (;;) {
                        string reply=Protocol.Reply(ReadLine(client),pin,authenticated);
                        if (reply==null) break;
                        SendLine(client,reply);
                        if (!authenticated) { authenticated=true; notify("Điện thoại đã kiểm tra kết nối thật. Trên Probe, chờ dịch vụ kết nối lại rồi thử thông báo ACB mới."); }
                    }
                } catch (Exception) { if (!stopped) notify("Điện thoại đã ngắt kết nối; helper vẫn chờ kết nối lại."); }
                finally { IntPtr old=Interlocked.Exchange(ref client,new IntPtr(-1)); if (old!=new IntPtr(-1)) Native.closesocket(old); }
            }
        }
        static string ReadLine(IntPtr connection) {
            var line=new StringBuilder(); var buffer=new byte[1];
            for (int i=0;i<512;i++) { int result=Native.recv(connection,buffer,1,0); if (result<=0) throw new IOException(); if (buffer[0]==10) return line.ToString(); if (buffer[0]<32 || buffer[0]>126) throw new IOException(); line.Append((char)buffer[0]); }
            throw new IOException();
        }
        static void SendLine(IntPtr connection,string line) {
            byte[] bytes=Encoding.ASCII.GetBytes(line+"\n"); int sent=0;
            while (sent<bytes.Length) { byte[] tail=new byte[bytes.Length-sent]; Array.Copy(bytes,sent,tail,0,tail.Length); int count=Native.send(connection,tail,tail.Length,0); if (count<=0) throw new IOException(); sent+=count; }
        }
        public void Dispose() {
            lock(this) {
            if (stopped) return; stopped=true;
            IntPtr old=Interlocked.Exchange(ref client,new IntPtr(-1)); if (old!=new IntPtr(-1)) Native.closesocket(old);
            old=Interlocked.Exchange(ref listener,new IntPtr(-1)); if (old!=new IntPtr(-1)) Native.closesocket(old);
            if (registered) { Native.WSASetService(ref query,2,0); registered=false; }
            if (radio!=IntPtr.Zero) { if (radioStateKnown) { if (!wasDiscoverable) Native.BluetoothEnableDiscovery(radio,false); if (!wasConnectable) Native.BluetoothEnableIncomingConnections(radio,false); } Native.CloseHandle(radio); radio=IntPtr.Zero; }
            foreach (var ptr in allocations) Marshal.FreeHGlobal(ptr); allocations.Clear();
            if (winsock) { Native.WSACleanup(); winsock=false; }
            }
        }
    }
    static class Native {
        [StructLayout(LayoutKind.Sequential,Pack=1)] internal struct BthAddress { internal ushort Family; internal ulong Address; internal Guid Service; internal uint Port; }
        [StructLayout(LayoutKind.Sequential)] internal struct SocketAddress { internal IntPtr Address; internal int Length; }
        [StructLayout(LayoutKind.Sequential)] internal struct CsAddress { internal SocketAddress Local,Remote; internal int SocketType,Protocol; }
        [StructLayout(LayoutKind.Sequential)] internal struct QuerySet { internal int Size; internal IntPtr Name,ServiceClass,Version,Comment; internal int Namespace; internal IntPtr Provider,Context; internal int ProtocolCount; internal IntPtr Protocols,Query; internal int AddressCount; internal IntPtr Addresses; internal int Flags; internal IntPtr Blob; }
        [StructLayout(LayoutKind.Sequential)] internal struct FindRadioParams { internal int Size; }
        [StructLayout(LayoutKind.Sequential,CharSet=CharSet.Unicode)] internal struct RadioInfo { internal int Size; internal ulong Address; [MarshalAs(UnmanagedType.ByValTStr,SizeConst=248)] internal string Name; internal uint Class; internal ushort Subversion,Manufacturer; }
        [DllImport("bthprops.cpl",SetLastError=true)] internal static extern IntPtr BluetoothFindFirstRadio(ref FindRadioParams parameters,out IntPtr radio);
        [DllImport("bthprops.cpl")] [return:MarshalAs(UnmanagedType.Bool)] internal static extern bool BluetoothFindRadioClose(IntPtr find);
        [DllImport("bthprops.cpl")] internal static extern int BluetoothGetRadioInfo(IntPtr radio,ref RadioInfo info);
        [DllImport("bthprops.cpl")] [return:MarshalAs(UnmanagedType.Bool)] internal static extern bool BluetoothIsDiscoverable(IntPtr radio);
        [DllImport("bthprops.cpl")] [return:MarshalAs(UnmanagedType.Bool)] internal static extern bool BluetoothIsConnectable(IntPtr radio);
        [DllImport("bthprops.cpl",SetLastError=true)] [return:MarshalAs(UnmanagedType.Bool)] internal static extern bool BluetoothEnableDiscovery(IntPtr radio,[MarshalAs(UnmanagedType.Bool)] bool value);
        [DllImport("bthprops.cpl",SetLastError=true)] [return:MarshalAs(UnmanagedType.Bool)] internal static extern bool BluetoothEnableIncomingConnections(IntPtr radio,[MarshalAs(UnmanagedType.Bool)] bool value);
        [DllImport("kernel32.dll")] [return:MarshalAs(UnmanagedType.Bool)] internal static extern bool CloseHandle(IntPtr handle);
        [DllImport("ws2_32.dll")] internal static extern int WSAStartup(ushort version,IntPtr data);
        [DllImport("ws2_32.dll")] internal static extern int WSACleanup();
        [DllImport("ws2_32.dll")] internal static extern int WSAGetLastError();
        [DllImport("ws2_32.dll")] internal static extern IntPtr socket(int family,int type,int protocol);
        [DllImport("ws2_32.dll")] internal static extern int bind(IntPtr socket,ref BthAddress address,int length);
        [DllImport("ws2_32.dll")] internal static extern int getsockname(IntPtr socket,ref BthAddress address,ref int length);
        [DllImport("ws2_32.dll")] internal static extern int listen(IntPtr socket,int backlog);
        [DllImport("ws2_32.dll")] internal static extern IntPtr accept(IntPtr socket,IntPtr address,IntPtr length);
        [DllImport("ws2_32.dll")] internal static extern int closesocket(IntPtr socket);
        [DllImport("ws2_32.dll")] internal static extern int setsockopt(IntPtr socket,int level,uint option,ref int value,int length);
        [DllImport("ws2_32.dll")] internal static extern int recv(IntPtr socket,byte[] buffer,int length,int flags);
        [DllImport("ws2_32.dll")] internal static extern int send(IntPtr socket,byte[] buffer,int length,int flags);
        [DllImport("ws2_32.dll",EntryPoint="WSASetServiceW",CharSet=CharSet.Unicode)] internal static extern int WSASetService(ref QuerySet query,int operation,uint flags);
    }
}
