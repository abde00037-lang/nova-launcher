using System;
using System.Runtime.CompilerServices;
using System.Security.Cryptography;
using System.Text;

namespace Nova.Core;

public static class OfflineIdentity
{
	public static string Uuid(string name)
	{
		byte[] array = MD5.HashData(Encoding.UTF8.GetBytes("OfflinePlayer:" + name));
		array[6] = (byte)((array[6] & 0xF) | 0x30);
		array[8] = (byte)((array[8] & 0x3F) | 0x80);
		string text = Convert.ToHexString(array).ToLowerInvariant();
		DefaultInterpolatedStringHandler defaultInterpolatedStringHandler = new DefaultInterpolatedStringHandler(4, 5);
		defaultInterpolatedStringHandler.AppendFormatted(text.Substring(0, 8));
		defaultInterpolatedStringHandler.AppendLiteral("-");
		defaultInterpolatedStringHandler.AppendFormatted(text.Substring(8, 4));
		defaultInterpolatedStringHandler.AppendLiteral("-");
		defaultInterpolatedStringHandler.AppendFormatted(text.Substring(12, 4));
		defaultInterpolatedStringHandler.AppendLiteral("-");
		defaultInterpolatedStringHandler.AppendFormatted(text.Substring(16, 4));
		defaultInterpolatedStringHandler.AppendLiteral("-");
		string text2 = text;
		defaultInterpolatedStringHandler.AppendFormatted(text2.Substring(20, text2.Length - 20));
		return defaultInterpolatedStringHandler.ToStringAndClear();
	}
}
