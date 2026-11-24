namespace Nova.Core;

public readonly record struct DownloadProgress(string Stage, int Current, int Total, long BytesDownloaded, string Detail)
{
	public double Fraction => (Total <= 0) ? 0.0 : ((double)Current / (double)Total);

	public double Percent => Fraction * 100.0;

	public double Megabytes => (double)BytesDownloaded / 1048576.0;
}
