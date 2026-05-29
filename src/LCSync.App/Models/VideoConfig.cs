namespace LCSync.Models;

public class VideoConfig
{
    public static readonly VideoConfig UltraLow = new()
    {
        Width = 640,
        Height = 360,
        Bitrate = 300_000,
        Framerate = 10,
        JpegQuality = 20,
        Name = "极低画质"
    };

    public static readonly VideoConfig Low360p = new()
    {
        Width = 640,
        Height = 360,
        Bitrate = 500_000,
        Framerate = 15,
        JpegQuality = 35,
        Name = "360P 低码率"
    };

    public static readonly VideoConfig Low480p = new()
    {
        Width = 854,
        Height = 480,
        Bitrate = 800_000,
        Framerate = 15,
        JpegQuality = 45,
        Name = "480P 标清"
    };

    public static readonly VideoConfig Medium = new()
    {
        Width = 1280,
        Height = 720,
        Bitrate = 1_500_000,
        Framerate = 20,
        JpegQuality = 60,
        Name = "720P 高清（推荐）"
    };

    public static readonly VideoConfig High = new()
    {
        Width = 1280,
        Height = 720,
        Bitrate = 2_500_000,
        Framerate = 25,
        JpegQuality = 75,
        Name = "720P 超清"
    };

    public static readonly VideoConfig FullHD = new()
    {
        Width = 1920,
        Height = 1080,
        Bitrate = 4_000_000,
        Framerate = 24,
        JpegQuality = 85,
        Name = "1080P 全高清"
    };

    public int Width { get; set; } = 1280;
    public int Height { get; set; } = 720;
    public int Bitrate { get; set; } = 1_500_000;
    public int Framerate { get; set; } = 20;
    public int JpegQuality { get; set; } = 60;
    public string Name { get; set; } = "720P 高清（推荐）";

    public string GetBitrateText()
    {
        if (Bitrate >= 1_000_000)
            return $"{Bitrate / 1_000_000}Mbps";
        else
            return $"{Bitrate / 1000}kbps";
    }
}
