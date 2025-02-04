using Microsoft.CodeAnalysis;

namespace SbModbus.SourceGenerator;

internal static class Utils
{
  public static int SizeOfType(ITypeSymbol typeSymbol)
  {
    var size = typeSymbol.TypeKind switch
    {
      // 枚举型
      TypeKind.Enum => GetEnumSize(typeSymbol),

      // Struct
      TypeKind.Struct => typeSymbol.SpecialType switch
      {
        SpecialType.System_Byte => 1,
        SpecialType.System_Int16 or SpecialType.System_UInt16 => 2,
        SpecialType.System_Int32 or SpecialType.System_UInt32 or SpecialType.System_Single => 4,
        SpecialType.System_Int64 or SpecialType.System_UInt64 or SpecialType.System_Double => 8,

        // 默认为0，即其他 struct
        _ => 0
      },

      _ => int.MinValue
    };

    return size;
  }

  private static int GetEnumSize(ITypeSymbol typeSymbol)
  {
    if (typeSymbol is INamedTypeSymbol { EnumUnderlyingType: ITypeSymbol underlyingType })
      return underlyingType.SpecialType switch
      {
        SpecialType.System_Byte => 1,
        SpecialType.System_Int16 or SpecialType.System_UInt16 => 2,
        SpecialType.System_Int32 or SpecialType.System_UInt32 => 4,
        SpecialType.System_Int64 or SpecialType.System_UInt64 => 8,
        _ => 0
      };

    return 0;
  }
}