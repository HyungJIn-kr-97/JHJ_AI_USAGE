namespace costats.Core.Pulse;

/// <summary>
/// 계정 구독 플랜 한 건 — 라벨 · 월 요금(USD) · 추정 여부 · 근거.
/// 계약: MonthlyUsd 가 null 이면 요금을 모르는 플랜이다. IsEstimate 가 true 면 화면에 ≈ 를 붙인다.
/// </summary>
public sealed record SubscriptionPlan(string Label, decimal? MonthlyUsd, bool IsEstimate, string Detail);

public static class SubscriptionPlans
{
    // 계약: 구독 요금의 출처와 확인일 — 화면 툴팁이 그대로 보인다. 요금이 바뀌면 둘을 함께 고친다
    public const string Source = "claude.com/pricing";
    public const string SourceDate = "2026-10-06";

    // 계약: subscriptionType · rateLimitTier 는 .credentials.json 의 값이다 — Max 는 등급(5x/20x)으로 요금이 갈리고, Team 은 좌석 등급·결제 주기를 API 가 안 줘 Standard 월납으로 추정한다
    public static SubscriptionPlan Claude(string? subscriptionType, string? rateLimitTier)
    {
        var type = (subscriptionType ?? string.Empty).Trim().ToLowerInvariant();
        var tier = (rateLimitTier ?? string.Empty).Trim();
        var lowerTier = tier.ToLowerInvariant();

        return type switch
        {
            "max" when lowerTier.Contains("20x") => new SubscriptionPlan("Max 20x", 200m, false, $"rateLimitTier={tier}"),
            "max" when lowerTier.Contains("5x") => new SubscriptionPlan("Max 5x", 100m, false, $"rateLimitTier={tier}"),
            "max" => new SubscriptionPlan("Max", null, true, "Tier not reported"),
            "pro" => new SubscriptionPlan("Pro", 20m, false, "Monthly billing"),
            "team" => new SubscriptionPlan("Team", 25m, true, "Standard seat, monthly billing — the API does not report seat type"),
            "enterprise" => new SubscriptionPlan("Enterprise", null, true, "Contract pricing"),
            "free" => new SubscriptionPlan("Free", 0m, false, string.Empty),
            "" => new SubscriptionPlan("Max", null, true, "No subscriptionType in credentials"),
            _ => new SubscriptionPlan(char.ToUpperInvariant(type[0]) + type[1..], null, true, string.Empty)
        };
    }
}
