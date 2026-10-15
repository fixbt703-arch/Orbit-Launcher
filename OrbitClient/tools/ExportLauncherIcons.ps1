param([Parameter(Mandatory=$true)][string]$SourceDirectory,[Parameter(Mandatory=$true)][string]$OutputDirectory)
$ErrorActionPreference='Stop'
Add-Type -AssemblyName PresentationCore,WindowsBase
[xml]$orbitIcons=Get-Content -LiteralPath (Join-Path $SourceDirectory 'softicons.xaml') -Raw
[xml]$orbitTheme=Get-Content -LiteralPath (Join-Path $SourceDirectory 'OrbitTheme.xaml') -Raw
$orbitKeyNamespace='http://schemas.microsoft.com/winfx/2006/xaml'
$orbitMap=@{settings='IconEB20';search='IconEB1C';close='IconEB55';left='IconEA60';right='IconEA61';all='IconEAC1';on='IconEA5E';check='IconEA5E';chunks='IconEEF7';zoom='IconEB56';clock='SmoothClockGeometry'}
New-Item -ItemType Directory -Path $OutputDirectory -Force | Out-Null
foreach($orbitName in $orbitMap.Keys){
    $orbitKey=$orbitMap[$orbitName]
    $orbitNode=@($orbitIcons.DocumentElement.ChildNodes)+@($orbitTheme.DocumentElement.ChildNodes) | Where-Object { $_ -is [Xml.XmlElement] -and $_.GetAttribute('Key',$orbitKeyNamespace) -eq $orbitKey } | Select-Object -First 1
    if($null -eq $orbitNode){throw "Missing launcher geometry: $orbitKey"}
    $orbitFigures=if($orbitNode.LocalName -eq 'PathGeometry'){@($orbitNode.GetAttribute('Figures'))}else{@($orbitNode.ChildNodes | Where-Object LocalName -eq 'PathGeometry' | ForEach-Object { $_.GetAttribute('Figures') })}
    $orbitPen=[Windows.Media.Pen]::new([Windows.Media.Brushes]::White,1.8)
    $orbitPen.StartLineCap='Round';$orbitPen.EndLineCap='Round';$orbitPen.LineJoin='Round'
    $orbitVisual=[Windows.Media.DrawingVisual]::new();$orbitDrawing=$orbitVisual.RenderOpen()
    $orbitDrawing.PushTransform([Windows.Media.ScaleTransform]::new(4,4))
    foreach($orbitFigure in $orbitFigures){$orbitDrawing.DrawGeometry($null,$orbitPen,[Windows.Media.Geometry]::Parse($orbitFigure))}
    $orbitDrawing.Pop();$orbitDrawing.Close()
    $orbitBitmap=[Windows.Media.Imaging.RenderTargetBitmap]::new(96,96,96,96,[Windows.Media.PixelFormats]::Pbgra32)
    $orbitBitmap.Render($orbitVisual)
    $orbitEncoder=[Windows.Media.Imaging.PngBitmapEncoder]::new();$orbitEncoder.Frames.Add([Windows.Media.Imaging.BitmapFrame]::Create($orbitBitmap))
    $orbitStream=[IO.File]::Create((Join-Path $OutputDirectory ($orbitName+'.png')))
    try{$orbitEncoder.Save($orbitStream)}finally{$orbitStream.Dispose()}
    [IO.File]::WriteAllText((Join-Path $OutputDirectory ($orbitName+'.png.mcmeta')),'{"texture":{"blur":true,"clamp":true}}',[Text.UTF8Encoding]::new($false))
}
Write-Output 'Exported 11 icons from the Orbit Launcher geometry source.'
