using System.Linq;
using System.Text;
using Microsoft.CodeAnalysis;
using Microsoft.CodeAnalysis.Text;
using static SbModbus.SourceGenerator.Utils;

namespace SbModbus.SourceGenerator;

public static class SbModbusArrayGenerator
{
  private const string SbModbusArrayAttributeName = "SbModbus.Attributes.SbModbusArrayAttribute";

  public static void Gen(GeneratorExecutionContext context, INamedTypeSymbol structSymbol)
  {
    var sbModbusArrayInfo = GetSbModbusInfo(structSymbol);
    if (sbModbusArrayInfo is null) return;

    var source = GenerateCodeForStruct(structSymbol, sbModbusArrayInfo);
    context.AddSource($"{structSymbol.Name}_ModbusArray.g.cs", SourceText.From(source, Encoding.UTF8));
  }

  private static string GenerateCodeForStruct(INamedTypeSymbol structSymbol, SbModbusArrayInfo arrayInfo)
  {
    var structName = structSymbol.Name;
    var isGlobalNamespace = structSymbol.ContainingNamespace.IsGlobalNamespace;
    var namespaceName = structSymbol.ContainingNamespace.ToDisplayString();
    var elementTypeName = arrayInfo.ElementType.ToDisplayString();
    var elementSize = arrayInfo.ElementSize;

    var sb = new StringBuilder();
    sb.AppendLine("// Auto-generated code");
    sb.AppendLine("#pragma warning disable");
    sb.AppendLine("using SbModbus.Utils;");
    sb.AppendLine("using System;");
    sb.AppendLine("using System.Runtime.CompilerServices;");
    sb.AppendLine("using System.Runtime.InteropServices;");
    sb.AppendLine("using static SbModbus.Utils.Utils;");
    if (!isGlobalNamespace)
    {
      sb.AppendLine($"namespace {namespaceName}");
      sb.AppendLine("{");
    }

    sb.AppendLine(
      $"[StructLayout(LayoutKind.Explicit, Pack = {elementSize}, Size = {elementSize * arrayInfo.Length})]");
    sb.AppendLine($"partial struct {structName}");
    sb.AppendLine("{");

    sb.AppendLine($"public {structName}(ReadOnlySpan<byte> data, byte mode = {arrayInfo.Mode})");
    sb.AppendLine("{");
    sb.AppendLine($"CheckLength(data, Unsafe.SizeOf<{structName}>());");
    for (var i = 0; i < arrayInfo.Length; i++)
    {
      var offset = elementSize * i;
      var s0 = elementSize switch
      {
        0 =>
          $"this._item{i} = new {elementTypeName}(data.Slice({offset}, Unsafe.SizeOf<{elementTypeName}>()), mode);",
        1 or 2 or 4 or 8 =>
          $"this._item{i} = data[{offset}..{offset + elementSize}].ToT<{elementTypeName}>(mode);",
        _ => string.Empty
      };
      sb.AppendLine(s0);
    }

    sb.AppendLine("}");

    sb.AppendLine($"public {structName}(ReadOnlySpan<ushort> data0, byte mode = {arrayInfo.Mode})");
    sb.AppendLine("{");
    sb.AppendLine("var data = data0.AsReadOnlyByteSpan();");
    sb.AppendLine($"CheckLength(data, Unsafe.SizeOf<{structName}>());");
    for (var i = 0; i < arrayInfo.Length; i++)
    {
      var offset = elementSize * i;
      var s0 = elementSize switch
      {
        0 =>
          $"this._item{i} = new {elementTypeName}(data.Slice({offset}, Unsafe.SizeOf<{elementTypeName}>()), mode);",
        1 or 2 or 4 or 8 =>
          $"this._item{i} = data[{offset}..{offset + elementSize}].ToT<{elementTypeName}>(mode);",
        _ => string.Empty
      };
      sb.AppendLine(s0);
    }

    sb.AppendLine("}");

    for (var i = 0; i < arrayInfo.Length; i++)
    {
      sb.Append($"[FieldOffset({i * arrayInfo.ElementSize})]");
      sb.AppendLine($"private {elementTypeName} _item{i};");
    }

    sb.AppendLine($"public byte[] ToBytes(byte mode = {arrayInfo.Mode})");
    sb.AppendLine("{");
    sb.AppendLine($"var data = new byte[Unsafe.SizeOf<{structName}>()];");
    sb.AppendLine("var span = data.AsSpan();");
    sb.AppendLine("WriteTo(span, mode);");
    sb.AppendLine("return data;");
    sb.AppendLine("}");
    sb.AppendLine($"public void WriteTo(Span<byte> span, byte mode = {arrayInfo.Mode})");
    sb.AppendLine("{");
    sb.AppendLine($"CheckLength(span, Unsafe.SizeOf<{structName}>());");
    for (var i = 0; i < arrayInfo.Length; i++)
    {
      var offset = elementSize * i;
      var s0 = elementSize switch
      {
        0 =>
          $"this._item{i}.WriteTo(span.Slice({offset}, Unsafe.SizeOf<{elementTypeName}>()), mode);",
        1 or 2 or 4 or 8 =>
          $"this._item{i}.WriteTo<{elementTypeName}>(span[{offset}..{offset + elementSize}], mode);",
        _ => string.Empty
      };
      sb.AppendLine(s0);
    }

    sb.AppendLine("}");

    sb.AppendLine($"public int Length => {arrayInfo.Length};");
    sb.AppendLine($"public {elementTypeName} this[int index]");
    sb.AppendLine("{");
    sb.AppendLine("get");
    sb.AppendLine("{");
    sb.AppendLine("return index switch {");
    for (var i = 0; i < arrayInfo.Length; i++) sb.AppendLine($"{i} => _item{i},");
    sb.AppendLine("_ => throw new IndexOutOfRangeException()");
    sb.AppendLine("};");
    sb.AppendLine("}");
    sb.AppendLine("set");
    sb.AppendLine("{");
    sb.AppendLine("switch (index)");
    sb.AppendLine("{");
    for (var i = 0; i < arrayInfo.Length; i++)
    {
      sb.AppendLine($"case {i}:");
      sb.AppendLine($"_item{i} = value;");
      sb.AppendLine("break;");
    }

    sb.AppendLine("default:");
    sb.AppendLine("throw new IndexOutOfRangeException();");
    sb.AppendLine("}");
    sb.AppendLine("}");
    sb.AppendLine("}");

    sb.AppendLine($"public Span<{elementTypeName}> AsSpan()");
    sb.AppendLine("{");
    sb.AppendLine($"return this.Cast<{structName}, {elementTypeName}>();");
    sb.AppendLine("}");
    
    sb.AppendLine($"public Span<{elementTypeName}> Slice(int start, int length)");
    sb.AppendLine("{");
    sb.AppendLine("var span = AsSpan();");
    sb.AppendLine("return span.Slice(start, length);");
    sb.AppendLine("}");

    sb.AppendLine($"public Span<{elementTypeName}> this[Range range]");
    sb.AppendLine("{");
    sb.AppendLine("get");
    sb.AppendLine("{");
    sb.AppendLine("var span = AsSpan();");
    sb.AppendLine("return span[range];");
    sb.AppendLine("}");
    sb.AppendLine("}");
    sb.AppendLine("}");
    if (!isGlobalNamespace) sb.AppendLine("}");
    return sb.ToString();
  }

  private static SbModbusArrayInfo? GetSbModbusInfo(INamedTypeSymbol structSymbol)
  {
    var sbModbusAttr = structSymbol.GetAttributes().FirstOrDefault(attr =>
      attr.AttributeClass != null && attr.AttributeClass.ToDisplayString() == SbModbusArrayAttributeName);

    if (sbModbusAttr?.ConstructorArguments[0].Value is INamedTypeSymbol type &&
        sbModbusAttr.ConstructorArguments[1].Value is int length
        && sbModbusAttr.ConstructorArguments[2].Value is byte mode)
    {
      var size = SizeOfType(type);
      return new SbModbusArrayInfo(type, size, length, mode);
    }

    return null;
  }
}

internal class SbModbusArrayInfo(INamedTypeSymbol elementType, int elementSize, int length, byte mode)
{
  public INamedTypeSymbol ElementType { get; } = elementType;

  public int ElementSize { get; } = elementSize;
  public int Length { get; } = length;

  public byte Mode { get; } = mode;
}