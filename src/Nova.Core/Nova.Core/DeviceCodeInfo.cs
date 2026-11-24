using System;

namespace Nova.Core;

public sealed class DeviceCodeInfo
{
	public string DeviceCode { get; set; } = "";

	public string UserCode { get; set; } = "";

	public string VerificationUri { get; set; } = "";

	public int IntervalSeconds { get; set; } = 5;

	public DateTime ExpiresUtc { get; set; }
}
