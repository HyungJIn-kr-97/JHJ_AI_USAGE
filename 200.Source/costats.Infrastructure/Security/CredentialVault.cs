using System.Net;
using AdysTech.CredentialManager;
using costats.Application.Security;

namespace costats.Infrastructure.Security;

public sealed class CredentialVault : ICredentialVault
{
    private const string TargetPrefix = "AI-Usage-Monitor_JHJ:";
    private const string Username = "AI-Usage-Monitor_JHJ";

    // 계약: 1.0.4 까지 쓰던 접두 — 읽을 때 한 번 옮겨 오는 데만 쓴다
    private const string LegacyTargetPrefix = "AiUsageMonitor:";

    public Task SaveAsync(string key, string secret, CancellationToken cancellationToken)
    {
        cancellationToken.ThrowIfCancellationRequested();
        var target = BuildTarget(key);
        var credential = new NetworkCredential(Username, secret);
        CredentialManager.SaveCredentials(target, credential, CredentialType.Generic);
        return Task.CompletedTask;
    }

    public Task<string?> LoadAsync(string key, CancellationToken cancellationToken)
    {
        cancellationToken.ThrowIfCancellationRequested();
        var target = BuildTarget(key);
        var credential = CredentialManager.GetCredentials(target);
        if (credential is null)
        {
            credential = TakeOverLegacy(key, target);
        }

        return Task.FromResult(credential?.Password);
    }

    // 왜: 1.0.4 까지는 「AiUsageMonitor:」 이름으로 저장했다 — 그대로 두면 이름이 바뀐 뒤 전원 다시 로그인해야 한다
    // 계약: 읽을 때 한 번 옮기고 옛 항목은 지운다 — 값은 그대로라 사용자는 아무것도 하지 않는다
    // TODO: 모든 PC 가 1.0.5 이상이 되면 지운다
    private static NetworkCredential? TakeOverLegacy(string key, string target)
    {
        try
        {
            var legacy = CredentialManager.GetCredentials(LegacyTargetPrefix + key.Trim());
            if (legacy?.Password is not { Length: > 0 })
            {
                return null;
            }

            CredentialManager.SaveCredentials(target, new NetworkCredential(Username, legacy.Password), CredentialType.Generic);
            CredentialManager.RemoveCredentials(LegacyTargetPrefix + key.Trim(), CredentialType.Generic);
            return legacy;
        }
        catch (Exception)
        {
            // 옮기지 못하면 없는 것으로 본다 — 사용자가 다시 연동하면 새 이름으로 저장된다
            return null;
        }
    }

    private static string BuildTarget(string key)
    {
        if (string.IsNullOrWhiteSpace(key))
        {
            throw new ArgumentException("Key is required.", nameof(key));
        }

        return $"{TargetPrefix}{key.Trim()}";
    }
}
