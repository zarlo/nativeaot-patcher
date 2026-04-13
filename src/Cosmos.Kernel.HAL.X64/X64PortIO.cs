using Cosmos.Kernel.Core.X64.Bridge;
using Cosmos.Kernel.HAL.Interfaces;

namespace Cosmos.Kernel.HAL.X64;

public class X64PortIO : IPortIO
{
    public byte ReadByte(ushort port) => PortIoNative.ReadByte(port);
    public ushort ReadWord(ushort port) => PortIoNative.ReadWord(port);
    public uint ReadDWord(ushort port) => PortIoNative.ReadDWord(port);

    public void WriteByte(ushort port, byte value) => PortIoNative.WriteByte(port, value);
    public void WriteWord(ushort port, ushort value) => PortIoNative.WriteWord(port, value);
    public void WriteDWord(ushort port, uint value) => PortIoNative.WriteDWord(port, value);
}
