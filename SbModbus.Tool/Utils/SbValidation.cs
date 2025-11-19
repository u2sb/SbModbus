using System.ComponentModel.DataAnnotations;
using System.Net;
using Sb.Extensions.System;

namespace SbModbus.Tool.Utils;

/// <summary>
///   验证器
/// </summary>
public static class SbValidation
{
  public static ValidationResult StringIsByte(string value, ValidationContext context)
  {
    var isValid = value.IsByte();

    return isValid ? ValidationResult.Success! : new ValidationResult("value is not byte");
  }

  public static ValidationResult StringIsUInt16(string value, ValidationContext context)
  {
    var isValid = value.IsUInt16();

    return isValid ? ValidationResult.Success! : new ValidationResult("value is not uint16");
  }

  public static ValidationResult StringIsInt32(string value, ValidationContext context)
  {
    var isValid = value.IsInt32();

    return isValid ? ValidationResult.Success! : new ValidationResult("value is not int32");
  }

  public static ValidationResult StringIsDouble(string value, ValidationContext context)
  {
    var isValid = value.IsDouble();

    return isValid ? ValidationResult.Success! : new ValidationResult("value is not double");
  }

  public static ValidationResult StringIsIp(string value, ValidationContext context)
  {
    var isValid = IPAddress.TryParse(value, out _);

    return isValid ? ValidationResult.Success! : new ValidationResult("value is not ip");
  }
}