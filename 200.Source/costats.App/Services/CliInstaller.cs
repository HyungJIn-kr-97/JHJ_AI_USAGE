using System.Diagnostics;
using System.IO;
using Microsoft.Win32;

namespace costats.App.Services;

/// <summary>
/// 연동에 쓸 공식 CLI(Claude Code · Codex)가 없을 때 winget 으로 깐다 — Git Center 의 gh 설치와 같은 길이다.
/// 계약: winget 은 보이는 콘솔로 띄운다 — UAC·진행 막대를 사용자가 그 창에서 본다. 호출한 쪽이 끝나기를 기다리고 CliLocator 로 다시 찾는다.
/// 함정: winget 이 깐 도구는 이 프로세스의 PATH 에 아직 없다 — RefreshProcessPath 로 레지스트리의 PATH 를 다시 실어야 찾는다.
/// </summary>
public static class CliInstaller
{
    public static string WingetIdOf(bool codex) => codex ? "OpenAI.Codex" : "Anthropic.ClaudeCode";

    public static string NameOf(bool codex) => codex ? "Codex" : "Claude Code";

    /// <summary>사용자가 손으로 칠 설치 명령 — 안내 문구에 넣는다.</summary>
    public static string CommandOf(bool codex) => "winget install --id " + WingetIdOf(codex) + " --exact";

    /// <summary>winget 콘솔을 띄운다 — 띄운 프로세스를 돌려주고(기다림·취소는 호출한 쪽), winget 이 없는 PC 면 null.</summary>
    public static Process? Start(bool codex)
    {
        var info = new ProcessStartInfo("winget.exe") { UseShellExecute = true };
        foreach (var arg in (string[])["install", "--id", WingetIdOf(codex), "--exact", "--source", "winget", "--accept-package-agreements", "--accept-source-agreements"])
        {
            info.ArgumentList.Add(arg);
        }

        try
        {
            return Process.Start(info);
        }
        catch (System.ComponentModel.Win32Exception)
        {
            return null;
        }
    }

    /// <summary>레지스트리의 PATH(장비 + 사용자)를 이 프로세스에 다시 싣는다.</summary>
    public static void RefreshProcessPath()
    {
        static string? Read(RegistryKey root, string subKey)
        {
            using var key = root.OpenSubKey(subKey);
            return key?.GetValue("Path", null, RegistryValueOptions.DoNotExpandEnvironmentNames) as string;
        }

        var parts = new[]
        {
            Read(Registry.LocalMachine, @"SYSTEM\CurrentControlSet\Control\Session Manager\Environment"),
            Read(Registry.CurrentUser, "Environment")
        }.Where(p => !string.IsNullOrWhiteSpace(p));
        var merged = string.Join(Path.PathSeparator, parts);
        if (merged.Length > 0)
        {
            Environment.SetEnvironmentVariable("PATH", Environment.ExpandEnvironmentVariables(merged), EnvironmentVariableTarget.Process);
        }
    }
}
