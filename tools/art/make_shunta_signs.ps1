$ErrorActionPreference = 'Stop'
Add-Type -AssemblyName System.Drawing
$destination = Join-Path $PSScriptRoot '../../Assets/Resources/ShuntaMetro/City/NeoTokyoSigns.png'
$bitmap = [Drawing.Bitmap]::new(2048,1024)
$graphics = [Drawing.Graphics]::FromImage($bitmap)
$graphics.TextRenderingHint = [Drawing.Text.TextRenderingHint]::AntiAliasGridFit
$graphics.SmoothingMode = [Drawing.Drawing2D.SmoothingMode]::AntiAlias
$font = [Drawing.Font]::new('Yu Gothic',38,[Drawing.FontStyle]::Bold)
$small = [Drawing.Font]::new('Arial',15,[Drawing.FontStyle]::Bold)
$format = [Drawing.StringFormat]::new()
$format.Alignment = [Drawing.StringAlignment]::Center
$format.LineAlignment = [Drawing.StringAlignment]::Center
$labels = @('新東京','ラーメン','電脳街','喫茶店','駅前','ゲーム','居酒屋','未来','音楽','カラオケ','ホテル','夜市場','書店','焼肉','薬局','鮨','東京湾','自転車','映画館','市場','食堂','電気','地下鉄','温泉','花屋','写真','レコード','茶屋','商店街','展望台','入口','営業中')
$subtitles = @('NEO TOKYO','RAMEN 24H','CYBER DISTRICT','COFFEE','STATION','ARCADE','IZAKAYA','MIRAI','LIVE MUSIC','KARAOKE','HOTEL','NIGHT MARKET','BOOKS','YAKINIKU','PHARMACY','SUSHI','TOKYO BAY','CYCLE WORKS','CINEMA','MARKET','DINER','ELECTRONICS','METRO','BATH HOUSE','FLOWERS','PHOTO','VINYL','TEA HOUSE','SHOPPING','SKY LOUNGE','ENTRANCE','OPEN 24H')
$palette = @('#FF6DAE','#66DEED','#FFD08B','#B4BFFF')
try {
    $graphics.Clear([Drawing.Color]::FromArgb(12,16,23))
    for ($i=0; $i -lt 32; $i++) {
        $x=($i % 8)*256; $y=[math]::Floor($i/8)*256
        $color=[Drawing.ColorTranslator]::FromHtml($palette[$i % 4])
        $brush=[Drawing.SolidBrush]::new($color)
        $pen=[Drawing.Pen]::new($color,3)
        $graphics.DrawRectangle($pen,$x+13,$y+13,230,230)
        $graphics.DrawLine($pen,$x+26,$y+177,$x+230,$y+177)
        $graphics.DrawString($labels[$i],$font,$brush,[Drawing.RectangleF]::new($x+14,$y+35,228,125),$format)
        $graphics.DrawString($subtitles[$i],$small,$brush,[Drawing.RectangleF]::new($x+14,$y+181,228,46),$format)
        $brush.Dispose(); $pen.Dispose()
    }
    $bitmap.Save([IO.Path]::GetFullPath($destination),[Drawing.Imaging.ImageFormat]::Png)
} finally { $format.Dispose(); $font.Dispose(); $small.Dispose(); $graphics.Dispose(); $bitmap.Dispose() }
Write-Output "Shunta signage atlas saved: $destination"
