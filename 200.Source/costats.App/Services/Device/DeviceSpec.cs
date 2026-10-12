using System.ComponentModel;
using System.Globalization;
using System.IO;
using System.Net.NetworkInformation;
using System.Net.Sockets;
using System.Runtime.InteropServices;
using System.Security;

namespace costats.App.Services;

/// <summary>
/// 이 앱이 깔린 장비의 사양 — snapshot.json 의 device.spec 로 나간다.
/// 계약: 칸의 뜻과 OS 별 채움 범위의 정본은 300.Docs\표시-데이터-저장소.md §3-5 다.
/// 계약: 못 읽은 칸은 null 로 빠진다 — 수집 실패가 화면 갱신과 저장을 막지 않는다.
/// 계약: OS 마다 다른 것은 DeviceProbe 를 상속해 채운다 — 이 파일은 .NET 공통 API 만 쓴다(Linux·macOS 에서도 그대로 돈다).
/// TODO: Linux(/proc · /sys/class/dmi/id) · macOS(sysctl · system_profiler) 탐침을 더하면 DeviceProbe.Create 에 한 줄씩 잇는다.
/// </summary>
public sealed record DeviceSpec(
    string Platform, DateTimeOffset CollectedAt, OsFacts Os, SystemFacts? System, CpuFacts Cpu, MemoryFacts? Memory,
    IReadOnlyList<GpuFacts> Gpus, IReadOnlyList<DiskFacts> Disks, IReadOnlyList<VolumeFacts> Volumes,
    IReadOnlyList<NetworkFacts> Networks, DisplayFacts? Display, PowerFacts? Power, SessionFacts Session, RuntimeFacts Runtime);

/// 계약: Build 는 「빌드.수정」 꼴(26200.6584), InstalledAt 은 OS 를 깐 시각이다.
public sealed record OsFacts(
    string Description, string Version, string? Edition = null, string? DisplayVersion = null, string? Build = null,
    DateTimeOffset? InstalledAt = null, string? MachineGuid = null);

public sealed record SystemFacts(
    string? Maker, string? Model, string? Sku, string? BiosVendor, string? BiosVersion, string? BiosDate,
    string? BoardMaker, string? Board);

public sealed record CpuFacts(
    string Arch, int LogicalCores, string? Name = null, string? Vendor = null, string? Identifier = null,
    int? PhysicalCores = null, int? MaxMhz = null);

public sealed record MemoryFacts(double TotalGb, double? AvailableGb = null);

public sealed record GpuFacts(string Name, string? DriverVersion = null, string? DriverDate = null, double? MemoryGb = null);

public sealed record DiskFacts(string Model);

public sealed record VolumeFacts(string Name, string? Label, string? Format, double TotalGb, double FreeGb);

public sealed record NetworkFacts(
    string Name, string Description, string Type, bool Up, string? Mac, long? SpeedMbps, IReadOnlyList<string> Ipv4);

/// 계약: 폭·높이는 물리 픽셀이다 — Virtual 은 모니터 전부를 이은 사각형이다.
public sealed record DisplayFacts(int Count, int PrimaryWidth, int PrimaryHeight, int VirtualWidth, int VirtualHeight);

public sealed record PowerFacts(bool HasBattery, bool? OnAc, int? BatteryPercent);

public sealed record SessionFacts(
    string OsUser, string Domain, bool? Elevated, string Culture, string UiCulture, string TimeZone,
    int UtcOffsetMinutes, DateTimeOffset BootedAt, string? SystemDrive);

public sealed record RuntimeFacts(string DotNet, string ProcessArch, string RuntimeId);

/// <summary>장비 사양을 읽는 탐침 — 바탕은 어느 OS 에서나 도는 .NET API 이고, OS 전용 탐침이 칸을 더 채운다.</summary>
public class DeviceProbe
{
    private static readonly Lazy<DeviceProbe> Instance = new(Create);

    public static DeviceProbe Current => Instance.Value;

    // 계약: OS 전용 탐침은 여기서만 고른다 — 부르는 쪽은 OS 를 묻지 않는다
    private static DeviceProbe Create() =>
        OperatingSystem.IsWindows() ? new WindowsDeviceProbe() : new DeviceProbe();

    public DeviceSpec Read() => new(
        Platform,
        DateTimeOffset.Now,
        Safe(ReadOs, new OsFacts(RuntimeInformation.OSDescription, Environment.OSVersion.Version.ToString())),
        Safe(ReadSystem, null),
        Safe(ReadCpu, new CpuFacts(RuntimeInformation.OSArchitecture.ToString(), Environment.ProcessorCount)),
        Safe(ReadMemory, null),
        Safe(ReadGpus, []),
        Safe(ReadDisks, []),
        Safe(ReadVolumes, []),
        Safe(ReadNetworks, []),
        Safe(ReadDisplay, null),
        Safe(ReadPower, null),
        ReadSession(),
        new RuntimeFacts(RuntimeInformation.FrameworkDescription, RuntimeInformation.ProcessArchitecture.ToString(),
            RuntimeInformation.RuntimeIdentifier));

    protected virtual string Platform =>
        OperatingSystem.IsLinux() ? "linux" : OperatingSystem.IsMacOS() ? "macos" : "other";

    protected virtual OsFacts ReadOs() =>
        new(RuntimeInformation.OSDescription, Environment.OSVersion.Version.ToString());

    protected virtual SystemFacts? ReadSystem() => null;

    protected virtual CpuFacts ReadCpu() =>
        new(RuntimeInformation.OSArchitecture.ToString(), Environment.ProcessorCount);

    // 함정: 컨테이너·작업 개체가 한도를 걸면 이 값은 실제 물리 메모리보다 작다 — OS 전용 탐침이 덮어쓴다
    protected virtual MemoryFacts? ReadMemory() =>
        GC.GetGCMemoryInfo().TotalAvailableMemoryBytes is > 0 and var total ? new MemoryFacts(Gb(total)) : null;

    protected virtual IReadOnlyList<GpuFacts> ReadGpus() => [];

    protected virtual IReadOnlyList<DiskFacts> ReadDisks() => [];

    // 함정: 네트워크·이동식 드라이브는 IsReady 가 오래 매달릴 수 있다 — 고정 디스크만 본다
    protected virtual IReadOnlyList<VolumeFacts> ReadVolumes() =>
        DriveInfo.GetDrives()
            .Where(drive => drive.DriveType == DriveType.Fixed && drive.IsReady)
            .Select(drive => new VolumeFacts(drive.Name, Blank(drive.VolumeLabel), Blank(drive.DriveFormat),
                Gb(drive.TotalSize), Gb(drive.TotalFreeSpace)))
            .ToList();

    protected virtual IReadOnlyList<NetworkFacts> ReadNetworks() =>
        NetworkInterface.GetAllNetworkInterfaces()
            .Where(nic => nic.NetworkInterfaceType is not (NetworkInterfaceType.Loopback or NetworkInterfaceType.Tunnel))
            .Where(nic => IsRealAdapter(nic.Description))
            .Select(nic => new NetworkFacts(
                nic.Name,
                nic.Description,
                nic.NetworkInterfaceType.ToString(),
                nic.OperationalStatus == OperationalStatus.Up,
                Mac(nic.GetPhysicalAddress()),
                nic.Speed > 0 ? nic.Speed / 1_000_000 : null,
                nic.GetIPProperties().UnicastAddresses
                    .Where(address => address.Address.AddressFamily == AddressFamily.InterNetwork)
                    .Select(address => address.Address.ToString())
                    .ToList()))
            .OrderByDescending(nic => nic.Up)
            .ToList();

    // 함정: Windows 는 어댑터마다 필터 드라이버 사본(「…-QoS Packet Scheduler-0000」)을 따로 내놓는다 — 실측 75개 중 51개가 그것이었다
    protected virtual bool IsRealAdapter(string description) => true;

    protected virtual DisplayFacts? ReadDisplay() => null;

    protected virtual PowerFacts? ReadPower() => null;

    protected virtual bool? IsElevated() => null;

    private SessionFacts ReadSession() => new(
        Environment.UserName,
        Environment.UserDomainName,
        Safe(IsElevated, null),
        CultureInfo.CurrentCulture.Name,
        CultureInfo.CurrentUICulture.Name,
        TimeZoneInfo.Local.Id,
        (int)TimeZoneInfo.Local.GetUtcOffset(DateTimeOffset.Now).TotalMinutes,
        // 왜: 초 단위로 자른다 — TickCount64 의 떨림이 갱신마다 다른 부팅 시각을 만든다
        Truncate(DateTimeOffset.Now - TimeSpan.FromMilliseconds(Environment.TickCount64)),
        Blank(Path.GetPathRoot(Environment.SystemDirectory)));

    protected static double Gb(long bytes) => Math.Round(bytes / 1024d / 1024d / 1024d, 2);

    protected static string? Blank(string? text) => string.IsNullOrWhiteSpace(text) ? null : text.Trim();

    private static string? Mac(PhysicalAddress address)
    {
        var bytes = address.GetAddressBytes();
        return bytes.Length == 0 || bytes.All(b => b == 0) ? null : string.Join(":", bytes.Select(b => b.ToString("X2")));
    }

    private static DateTimeOffset Truncate(DateTimeOffset at) => at.AddTicks(-(at.Ticks % TimeSpan.TicksPerSecond));

    // 계약: 한 묶음이 실패해도 나머지는 남긴다 — 권한·드라이버에 따라 일부만 읽히는 장비가 있다
    private static T Safe<T>(Func<T> read, T fallback)
    {
        try
        {
            return read();
        }
        catch (Exception ex) when (ex is IOException or UnauthorizedAccessException or SecurityException
            or NetworkInformationException or Win32Exception or InvalidOperationException
            or PlatformNotSupportedException or NotSupportedException or FormatException or InvalidCastException)
        {
            return fallback;
        }
    }
}
