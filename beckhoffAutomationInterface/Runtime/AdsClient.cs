using System;
using System.Runtime.InteropServices;
using System.Text;

namespace BeckhoffAutomationInterface.Runtime
{
    /// <summary>
    /// Minimal ADS client over Beckhoff's native TcAdsDll (installed with TwinCAT; the 64-bit
    /// one, because this tool is x64). No NuGet dependency. Proven against UmRT by the
    /// BeckhoffLib VariantDemo prototype (2026-10-14).
    /// </summary>
    sealed class AdsClient : IDisposable
    {
        const string Dll = @"C:\Program Files (x86)\Beckhoff\TwinCAT\Common64\TcAdsDll.dll";

        public const ushort PortSystemService = 10000;
        public const ushort PortPlc1 = 851;
        public const ushort StateRun = 5;
        public const ushort StateConfig = 15;
        public const int ErrorSymbolNotFound = 1808;   // ADSERR_DEVICE_SYMBOLNOTFOUND

        const uint SymHandleByName = 0xF003, SymValueByHandle = 0xF005, SymReleaseHandle = 0xF006;

        [StructLayout(LayoutKind.Sequential, Pack = 1)]
        struct AmsAddr
        {
            [MarshalAs(UnmanagedType.ByValArray, SizeConst = 6)] public byte[] NetId;
            public ushort Port;
        }

        [DllImport(Dll)] static extern int AdsPortOpenEx();
        [DllImport(Dll)] static extern int AdsPortCloseEx(int port);
        [DllImport(Dll)] static extern int AdsGetLocalAddressEx(int port, ref AmsAddr addr);
        [DllImport(Dll)] static extern int AdsSyncReadStateReqEx(int port, ref AmsAddr addr, out ushort adsState, out ushort deviceState);
        [DllImport(Dll)] static extern int AdsSyncReadWriteReqEx2(int port, ref AmsAddr addr, uint indexGroup, uint indexOffset,
            uint readLength, byte[] readData, uint writeLength, byte[] writeData, out uint bytesRead);
        [DllImport(Dll)] static extern int AdsSyncReadReqEx2(int port, ref AmsAddr addr, uint indexGroup, uint indexOffset,
            uint length, byte[] data, out uint bytesRead);
        [DllImport(Dll)] static extern int AdsSyncWriteReqEx(int port, ref AmsAddr addr, uint indexGroup, uint indexOffset,
            uint length, byte[] data);

        readonly int _port;
        AmsAddr _addr;

        public string NetId { get; }
        public ushort Port => _addr.Port;

        public AdsClient(string netId, ushort port)
        {
            _port = AdsPortOpenEx();
            if (_port == 0)
                throw new InvalidOperationException("AdsPortOpenEx failed (is TwinCAT installed and the router running?)");
            NetId = netId;
            _addr = new AmsAddr { NetId = ParseNetId(netId), Port = port };
        }

        public void Dispose() => AdsPortCloseEx(_port);

        /// <summary>This PC's own AMS Net ID, as the local ADS router reports it.</summary>
        public static string LocalNetId()
        {
            int port = AdsPortOpenEx();
            if (port == 0)
                throw new InvalidOperationException("AdsPortOpenEx failed (is TwinCAT installed and the router running?)");
            try
            {
                var addr = new AmsAddr { NetId = new byte[6] };
                int err = AdsGetLocalAddressEx(port, ref addr);
                if (err != 0)
                    throw new InvalidOperationException("AdsGetLocalAddressEx failed, ADS error " + err);
                return FormatNetId(addr.NetId);
            }
            finally
            {
                AdsPortCloseEx(port);
            }
        }

        /// <summary>ADS error code (0 = ok); state is valid only when 0 is returned.</summary>
        public int TryReadState(out ushort state) => AdsSyncReadStateReqEx(_port, ref _addr, out state, out ushort _);

        /// <summary>Reads a symbol's raw bytes by name. Returns the ADS error code (0 = ok);
        /// ErrorSymbolNotFound when the symbol is not in the running program.</summary>
        public int TryRead(string symbol, int size, out byte[] value)
        {
            value = null;
            byte[] name = Encoding.ASCII.GetBytes(symbol + "\0");
            byte[] handle = new byte[4];
            int err = AdsSyncReadWriteReqEx2(_port, ref _addr, SymHandleByName, 0, 4, handle, (uint)name.Length, name, out uint _);
            if (err != 0)
                return err;
            try
            {
                var data = new byte[size];
                err = AdsSyncReadReqEx2(_port, ref _addr, SymValueByHandle, BitConverter.ToUInt32(handle, 0), (uint)size, data, out uint _);
                if (err == 0)
                    value = data;
                return err;
            }
            finally
            {
                AdsSyncWriteReqEx(_port, ref _addr, SymReleaseHandle, 0, 4, handle);
            }
        }

        public static string StateName(ushort state)
        {
            switch (state)
            {
                case 5: return "Run";
                case 6: return "Stop";
                case 15: return "Config";
                case 16: return "Reconfig";
                default: return "state " + state;
            }
        }

        internal static byte[] ParseNetId(string netId)
        {
            string[] parts = (netId ?? "").Split('.');
            if (parts.Length != 6)
                throw new ArgumentException("not an AMS Net ID (a.b.c.d.e.f): " + netId);
            var bytes = new byte[6];
            for (int i = 0; i < 6; i++)
                if (!byte.TryParse(parts[i], out bytes[i]))
                    throw new ArgumentException("not an AMS Net ID (a.b.c.d.e.f): " + netId);
            return bytes;
        }

        internal static string FormatNetId(byte[] bytes) => string.Join(".", bytes);
    }
}
