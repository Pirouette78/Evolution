Add-Type -AssemblyName System.Drawing
$img = [System.Drawing.Bitmap]::FromFile("C:\unityProjects\Evolution\Assets\wangbl.png")
Write-Host "0,0: " $img.GetPixel(0,0)
Write-Host "16,16: " $img.GetPixel(16,16)
Write-Host "16,0: " $img.GetPixel(16,0)
$img.Dispose()
