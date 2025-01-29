// ReSharper disable IdentifierTypo
// ReSharper disable InconsistentNaming

namespace SbModbus.Models
{
  public enum BigAndSmallEndianEncodingMode : byte
  {
    /// <summary>
    ///   小端模式
    /// </summary>
    DCBA = 0,

    /// <summary>
    ///   大端模式
    /// </summary>
    ABCD = 1,
    BADC = 2,
    CDAB = 3
  }
}