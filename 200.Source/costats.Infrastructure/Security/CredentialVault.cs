using costats.Application.Security;
using CoreVault = Jhj.Core.Windows.Security.CredentialVault;

namespace costats.Infrastructure.Security;

/// <summary>
/// Windows 자격 증명 관리자에 비밀값을 둔다 — 대상 이름은 「JHJ_AI-Usage-Monitor:&lt;키&gt;」다.
/// 계약: 옛 이름(JhjApp.LegacyNames) 항목을 읽을 때 한 번 옮겨 오는 것도 JHJ_CS_CORE 가 한다 —
///       이름이 바뀌어도 사용자가 다시 로그인하지 않는다.
/// </summary>
public sealed class CredentialVault : ICredentialVault
{
    private readonly CoreVault _vault = new();

    public Task SaveAsync(string key, string secret, CancellationToken cancellationToken) =>
        _vault.SaveAsync(key, secret, cancellationToken);

    public Task<string?> LoadAsync(string key, CancellationToken cancellationToken) =>
        _vault.LoadAsync(key, cancellationToken);
}
