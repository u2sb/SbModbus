using System.Collections.Generic;
using System.IO;
using System.IO.Ports;
using System.Threading.Tasks;
using SbModbus.Tool.Services.DataTransferServices;
using SbModbus.Tool.Services.ModbusServices;
using VYaml.Annotations;
using VYaml.Serialization;

namespace SbModbus.Tool.Models.Settings;

[YamlObject]
public partial class AppSettings
{
  #region 字段

  [YamlMember] public string Lang { get; set; }

  [YamlMember] public int DsLogMaxShow { get; set; }

  [YamlMember] public SerialPortAssistantPageSettings SerialPortAssistant { get; set; } = new();

  [YamlMember] public ModbusPageSettings Modbus { get; set; } = new();

  [YamlIgnore] public Dictionary<string, string> LanguageMap { get; }

  #endregion

  #region 初始化和保存

  [YamlConstructor]
  private AppSettings(string lang = LanguageName.ZhCN)
  {
    Lang = lang;

    LanguageMap = new Dictionary<string, string>();

    if (File.Exists(LangBaseFilePath))
    {
      var lb = File.ReadAllBytes(LangBaseFilePath);
      var b0 = YamlSerializer.Deserialize<Dictionary<string, string>?>(lb) ?? new Dictionary<string, string>();
      foreach (var (key, value) in b0) LanguageMap[key] = value;
    }

    if (File.Exists(LangFilePath(lang)))
    {
      var l = File.ReadAllBytes(LangFilePath(lang));
      var b1 = YamlSerializer.Deserialize<Dictionary<string, string>?>(l) ?? new Dictionary<string, string>();
      foreach (var (key, value) in b1) LanguageMap[key] = value;
    }
  }

  // ReSharper disable InconsistentNaming

  private const string SettingsDirectory = "Settings";
  private const string LangDirectory = "i18n";


  private static readonly string AppSettingsFilePath = Path.Combine(SettingsDirectory, "app.yml");


  private static readonly string LangBaseFilePath = Path.Combine(LangDirectory, "base.yml");

  private static string LangFilePath(string l)
  {
    return Path.Combine(LangDirectory, $"{l}.yml");
  }

  // ReSharper restore InconsistentNaming

  private static AppSettings? _instance;

  public static AppSettings Instance
  {
    get
    {
      if (_instance == null)
      {
        if (File.Exists(AppSettingsFilePath))
        {
          var sb = File.ReadAllBytes(AppSettingsFilePath);
          var b = YamlSerializer.Deserialize<AppSettings?>(sb) ?? new AppSettings();
          _instance = b;
        }
        else
        {
          _instance = new AppSettings();
        }
      }

      return _instance;
    }
  }

  /// <summary>
  ///   保存
  /// </summary>
  public async ValueTask SaveAsync()
  {
    await using var fs = File.Create(AppSettingsFilePath);
    var bs = YamlSerializer.Serialize(this);
    await fs.WriteAsync(bs);
  }

  #endregion
}

[YamlObject]
public partial class SerialPortAssistantPageSettings
{
  /// <summary>
  ///   传输类型
  /// </summary>
  public DataTransferType DataTransferType { get; set; }

  /// <summary>
  ///   串口号
  /// </summary>
  public string? SerialPortName { get; set; } = "COM1";

  /// <summary>
  ///   波特率
  /// </summary>
  public int BaudRate { get; set; } = 19200;

  /// <summary>
  ///   数据位
  /// </summary>
  public int DataBits { get; set; } = 8;

  /// <summary>
  ///   停止位
  /// </summary>
  public StopBits StopBits { get; set; } = StopBits.One;

  /// <summary>
  ///   校验位
  /// </summary>
  public Parity Parity { get; set; } = Parity.None;

  /// <summary>
  ///   流控
  /// </summary>
  public Handshake Handshake { get; set; } = Handshake.None;

  /// <summary>
  ///   目标 IP
  /// </summary>
  public string TargetIp { get; set; } = "127.0.0.1";

  /// <summary>
  ///   本地 IP
  /// </summary>
  public string LocalIp { get; set; } = "0.0.0.0";

  /// <summary>
  ///   目标端口
  /// </summary>
  public ushort TargetPort { get; set; } = 13567;

  /// <summary>
  ///   本地端口
  /// </summary>
  public ushort LocalPort { get; set; } = 13567;
}

[YamlObject]
public partial class ModbusPageSettings
{
  /// <summary>
  ///   传输类型
  /// </summary>
  public ModbusClientType ModbusClientType { get; set; }

  /// <summary>
  ///   串口号
  /// </summary>
  public string? SerialPortName { get; set; } = "COM1";

  /// <summary>
  ///   波特率
  /// </summary>
  public int BaudRate { get; set; } = 19200;

  /// <summary>
  ///   数据位
  /// </summary>
  public int DataBits { get; set; } = 8;

  /// <summary>
  ///   停止位
  /// </summary>
  public StopBits StopBits { get; set; } = StopBits.One;

  /// <summary>
  ///   校验位
  /// </summary>
  public Parity Parity { get; set; } = Parity.None;

  /// <summary>
  ///   流控
  /// </summary>
  public Handshake Handshake { get; set; } = Handshake.None;

  /// <summary>
  ///   目标 IP
  /// </summary>
  public string TargetIp { get; set; } = "127.0.0.1";

  /// <summary>
  ///   目标端口
  /// </summary>
  public ushort TargetPort { get; set; } = 13567;

  /// <summary>
  ///   站号
  /// </summary>
  public byte StationId { get; set; } = 0;
}