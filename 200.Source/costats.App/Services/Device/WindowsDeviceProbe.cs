using System.Globalization;
using System.Runtime.InteropServices;
using System.Runtime.Versioning;
using System.Security.Principal;
using Microsoft.Win32;

namespace costats.App.Services;

/// <summary>
/// Windows 전용 탐침 — 레지스트리와 Win32 호출로 사양 칸을 채운다.
/// 왜: WMI(System.Management)를 쓰지 않는다 — 단일 파일 게시에 꾸러미가 하나 더 붙고 조회 한 번이 1초를 넘는다.
/// 함정: 메모리 속도·슬롯 수는 SMBIOS 를 읽어야 해서 담지 않는다.
/// </summary>
[SupportedOSPlatform("windows")]
public sealed class WindowsDeviceProbe : DeviceProbe
{
    private const string OsKey = @"SOFTWARE\Microsoft\Windows NT\CurrentVersion";
    private const string BiosKey = @"HARDWARE\DESCRIPTION\System\BIOS";
    private const string CpuKey = @"HARDWARE\DESCRIPTION\System\CentralProcessor\0";
    private const string DisplayClassKey = @"SYSTEM\CurrentControlSet\Control\Class\{4d36e968-e325-11ce-bfc1-08002be10318}";
    private const string DiskEnumKey = @"SYSTEM\CurrentControlSet\Services\disk\Enum";
    private const string DeviceEnumKey = @"SYSTEM\CurrentControlSet\Enum\";

    protected override string Platform => "windows";

    protected override OsFacts ReadOs()
    {
        var build = Text(OsKey, "CurrentBuild");
        var edition = Text(OsKey, "ProductName");
        // 함정: Windows 11 도 ProductName 은 「Windows 10 …」 이다 — 빌드 22000 부터 11 로 고쳐 적는다
        if (edition is not null && int.TryParse(build, out var number) && number >= 22000)
        {
            edition = edition.Replace("Windows 10", "Windows 11", StringComparison.Ordinal);
        }

        return new OsFacts(
            RuntimeInformation.OSDescription,
            Environment.OSVersion.Version.ToString(),
            edition,
            Text(OsKey, "DisplayVersion"),
            build is null ? null : Number(OsKey, "UBR") is { } revision ? $"{build}.{revision}" : build,
            Number(OsKey, "InstallDate") is > 0 and var seconds ? DateTimeOffset.FromUnixTimeSeconds(seconds).ToLocalTime() : null,
            Text(@"SOFTWARE\Microsoft\Cryptography", "MachineGuid"));
    }

    protected override SystemFacts? ReadSystem() => new(
        Text(BiosKey, "SystemManufacturer"),
        Text(BiosKey, "SystemProductName"),
        Text(BiosKey, "SystemSKU"),
        Text(BiosKey, "BIOSVendor"),
        Text(BiosKey, "BIOSVersion"),
        Text(BiosKey, "BIOSReleaseDate"),
        Text(BiosKey, "BaseBoardManufacturer"),
        Text(BiosKey, "BaseBoardProduct"));

    protected override CpuFacts ReadCpu() => new(
        RuntimeInformation.OSArchitecture.ToString(),
        Environment.ProcessorCount,
        Text(CpuKey, "ProcessorNameString"),
        Text(CpuKey, "VendorIdentifier"),
        Text(CpuKey, "Identifier"),
        PhysicalCores(),
        Number(CpuKey, "~MHz") is > 0 and var mhz ? (int)mhz : null);

    protected override MemoryFacts? ReadMemory()
    {
        var status = new MemoryStatusEx { Length = (uint)Marshal.SizeOf<MemoryStatusEx>() };
        return GlobalMemoryStatusEx(ref status)
            ? new MemoryFacts(Gb((long)status.TotalPhys), Gb((long)status.AvailPhys))
            : base.ReadMemory();
    }

    protected override IReadOnlyList<GpuFacts> ReadGpus()
    {
        var gpus = new List<GpuFacts>();
        using var root = Registry.LocalMachine.OpenSubKey(DisplayClassKey);
        // 함정: 이 키 아래 Properties 는 일반 사용자가 못 연다 — 숫자 네 자리 하위 키만 연다
        foreach (var name in root?.GetSubKeyNames().Where(n => n.Length == 4 && n.All(char.IsDigit)) ?? [])
        {
            using var key = root!.OpenSubKey(name);
            if (Blank(key?.GetValue("DriverDesc") as string) is not { } description)
            {
                continue;
            }

            var memory = Bytes(key!.GetValue("HardwareInformation.qwMemorySize"))
                ?? Bytes(key.GetValue("HardwareInformation.MemorySize"));
            gpus.Add(new GpuFacts(description, Blank(key.GetValue("DriverVersion") as string),
                Blank(key.GetValue("DriverDate") as string), memory is > 0 ? Gb(memory.Value) : null));
        }

        // 함정: 원격 세션마다 「Microsoft Remote Display Adapter」 가 하나씩 쌓인다 — 같은 이름·드라이버는 한 줄로 접는다
        return gpus.Distinct().ToList();
    }

    protected override bool IsRealAdapter(string description) =>
        !(description.Length > 5 && description[^5] == '-' && description[^4..].All(char.IsDigit))
        && !description.StartsWith("WAN Miniport", StringComparison.OrdinalIgnoreCase)
        && !description.StartsWith("RAS Async", StringComparison.OrdinalIgnoreCase)
        && !description.StartsWith("Microsoft Kernel Debug", StringComparison.OrdinalIgnoreCase);

    protected override IReadOnlyList<DiskFacts> ReadDisks()
    {
        var disks = new List<DiskFacts>();
        var count = Number(DiskEnumKey, "Count") ?? 0;
        for (var index = 0; index < count; index++)
        {
            // 계약: 값은 장치 인스턴스 경로다(SCSI\Disk&Ven_…\…) — 사람이 읽는 이름은 Enum 아래 FriendlyName 에 있다
            if (Text(DiskEnumKey, index.ToString(CultureInfo.InvariantCulture)) is { } instance
                && Text(DeviceEnumKey + instance, "FriendlyName") is { } model)
            {
                disks.Add(new DiskFacts(model));
            }
        }

        return disks;
    }

    protected override DisplayFacts? ReadDisplay() => new(
        GetSystemMetrics(80), GetSystemMetrics(0), GetSystemMetrics(1), GetSystemMetrics(78), GetSystemMetrics(79));

    protected override PowerFacts? ReadPower()
    {
        if (!GetSystemPowerStatus(out var status))
        {
            return null;
        }

        // 계약: BatteryFlag 128 = 배터리 없음, 255 = 모름 — 그 밖이면 배터리가 달린 장비다
        var hasBattery = status.BatteryFlag is not (128 or 255);
        return new PowerFacts(
            hasBattery,
            status.AcLineStatus switch { 0 => false, 1 => true, _ => null },
            hasBattery && status.BatteryLifePercent <= 100 ? status.BatteryLifePercent : null);
    }

    protected override bool? IsElevated()
    {
        using var identity = WindowsIdentity.GetCurrent();
        return new WindowsPrincipal(identity).IsInRole(WindowsBuiltInRole.Administrator);
    }

    // 함정: 논리 프로세서가 64개를 넘는 장비는 프로세서 그룹 하나만 센다
    private static int? PhysicalCores()
    {
        uint length = 0;
        GetLogicalProcessorInformation(IntPtr.Zero, ref length);
        if (length == 0)
        {
            return null;
        }

        var buffer = Marshal.AllocHGlobal((int)length);
        try
        {
            if (!GetLogicalProcessorInformation(buffer, ref length))
            {
                return null;
            }

            // 계약: 한 칸은 마스크(포인터 크기) + 관계(4) + 정렬 + 공용체(16) 다 — 관계 0 이 물리 코어 하나다
            var size = IntPtr.Size == 8 ? 32 : 24;
            var cores = 0;
            for (var offset = 0; offset + size <= length; offset += size)
            {
                if (Marshal.ReadInt32(buffer, offset + IntPtr.Size) == 0)
                {
                    cores++;
                }
            }

            return cores > 0 ? cores : null;
        }
        finally
        {
            Marshal.FreeHGlobal(buffer);
        }
    }

    private static string? Text(string path, string name)
    {
        using var key = Registry.LocalMachine.OpenSubKey(path);
        return Blank(key?.GetValue(name) as string);
    }

    private static long? Number(string path, string name)
    {
        using var key = Registry.LocalMachine.OpenSubKey(path);
        return key?.GetValue(name) switch
        {
            int value => (uint)value,
            long value => value,
            _ => null
        };
    }

    // 함정: 드라이버마다 형식이 다르다 — QWORD · DWORD · 4/8바이트 이진 셋이 다 온다
    private static long? Bytes(object? value) => value switch
    {
        long number => number,
        int number => (uint)number,
        byte[] { Length: >= 8 } raw => BitConverter.ToInt64(raw, 0),
        byte[] { Length: >= 4 } raw => BitConverter.ToUInt32(raw, 0),
        _ => null
    };

    [StructLayout(LayoutKind.Sequential)]
    private struct MemoryStatusEx
    {
        public uint Length;
        public uint MemoryLoad;
        public ulong TotalPhys;
        public ulong AvailPhys;
        public ulong TotalPageFile;
        public ulong AvailPageFile;
        public ulong TotalVirtual;
        public ulong AvailVirtual;
        public ulong AvailExtendedVirtual;
    }

    [StructLayout(LayoutKind.Sequential)]
    private struct SystemPowerStatus
    {
        public byte AcLineStatus;
        public byte BatteryFlag;
        public byte BatteryLifePercent;
        public byte SystemStatusFlag;
        public uint BatteryLifeTime;
        public uint BatteryFullLifeTime;
    }

    [DllImport("kernel32.dll", SetLastError = true)]
    [return: MarshalAs(UnmanagedType.Bool)]
    private static extern bool GlobalMemoryStatusEx(ref MemoryStatusEx status);

    [DllImport("kernel32.dll", SetLastError = true)]
    [return: MarshalAs(UnmanagedType.Bool)]
    private static extern bool GetLogicalProcessorInformation(IntPtr buffer, ref uint length);

    [DllImport("kernel32.dll", SetLastError = true)]
    [return: MarshalAs(UnmanagedType.Bool)]
    private static extern bool GetSystemPowerStatus(out SystemPowerStatus status);

    [DllImport("user32.dll")]
    private static extern int GetSystemMetrics(int index);
}
