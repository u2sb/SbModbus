param(
  [Parameter(Mandatory = $true)]
  [Alias("v")]
  [string]$Version
)

# 定义要打包的项目文件路径
$projects = @(
  "SbModbus\SbModbus.csproj",
  "SbModbus.SerialPortStream\SbModbus.SerialPortStream.csproj",
  "SbModbus.TcpStream\SbModbus.TcpStream.csproj"
)

# 定义输出目录
$outputDir = "build"

# 确保输出目录存在
if (!(Test-Path $outputDir)) {
  New-Item -ItemType Directory -Path $outputDir -Force
}

# 遍历并打包每个项目
foreach ($project in $projects) {
  # 检查项目文件是否存在
  if (Test-Path $project) {
    Write-Host "正在打包项目: $project (版本: $Version)" -ForegroundColor Green
        
    # 执行 dotnet pack 命令，添加版本参数
    dotnet pack $project -o $outputDir --configuration Release -p:PackageVersion=$Version
        
    # 检查上一个命令是否执行成功
    if ($LASTEXITCODE -eq 0) {
      Write-Host "成功打包: $project" -ForegroundColor Cyan
    }
    else {
      Write-Host "打包失败: $project" -ForegroundColor Red
    }
  }
  else {
    Write-Host "项目文件不存在: $project" -ForegroundColor Yellow
  }
}

Write-Host "所有项目打包完成！输出目录: $($PWD.Path)\$outputDir" -ForegroundColor Green