using System.Runtime.InteropServices;
using System.Text;
using AutoCategory.Core.Platforms.Kick;

namespace AutoCategory.App.Platform;

/// <summary>
/// הצפנת סודות עם Windows DPAPI: רק אותו משתמש Windows, על אותו מחשב, יכול לפענח.
/// (P/Invoke ישיר ל-crypt32 – בלי חבילות חיצוניות.)
/// </summary>
public sealed class DpapiSecretProtector : ISecretProtector
{
    private static readonly byte[] Entropy = Encoding.UTF8.GetBytes("BengoAutoCategory.Kick.v1");

    public string Protect(string plain)
    {
        var data = Encoding.UTF8.GetBytes(plain);
        return "dpapi:" + Convert.ToBase64String(Run(data, protect: true));
    }

    public string Unprotect(string protectedValue)
    {
        if (!protectedValue.StartsWith("dpapi:", StringComparison.Ordinal))
            throw new CryptographicException("Unknown format");
        var data = Convert.FromBase64String(protectedValue[6..]);
        return Encoding.UTF8.GetString(Run(data, protect: false));
    }

    private static byte[] Run(byte[] input, bool protect)
    {
        var inBlob = new DataBlob();
        var entropyBlob = new DataBlob();
        var outBlob = new DataBlob();
        var inHandle = GCHandle.Alloc(input, GCHandleType.Pinned);
        var entHandle = GCHandle.Alloc(Entropy, GCHandleType.Pinned);
        try
        {
            inBlob.cbData = input.Length;
            inBlob.pbData = inHandle.AddrOfPinnedObject();
            entropyBlob.cbData = Entropy.Length;
            entropyBlob.pbData = entHandle.AddrOfPinnedObject();

            var ok = protect
                ? CryptProtectData(ref inBlob, "BengoAutoCategory", ref entropyBlob, IntPtr.Zero, IntPtr.Zero, UiForbidden, ref outBlob)
                : CryptUnprotectData(ref inBlob, IntPtr.Zero, ref entropyBlob, IntPtr.Zero, IntPtr.Zero, UiForbidden, ref outBlob);
            if (!ok) throw new CryptographicException("DPAPI error " + Marshal.GetLastWin32Error());

            var result = new byte[outBlob.cbData];
            Marshal.Copy(outBlob.pbData, result, 0, outBlob.cbData);
            return result;
        }
        finally
        {
            inHandle.Free();
            entHandle.Free();
            if (outBlob.pbData != IntPtr.Zero) LocalFree(outBlob.pbData);
        }
    }

    public sealed class CryptographicException : Exception
    {
        public CryptographicException(string message) : base(message) { }
    }

    private const int UiForbidden = 0x1;

    [StructLayout(LayoutKind.Sequential)]
    private struct DataBlob
    {
        public int cbData;
        public IntPtr pbData;
    }

    [DllImport("crypt32.dll", SetLastError = true, CharSet = CharSet.Unicode)]
    private static extern bool CryptProtectData(ref DataBlob dataIn, string description, ref DataBlob entropy,
        IntPtr reserved, IntPtr prompt, int flags, ref DataBlob dataOut);

    [DllImport("crypt32.dll", SetLastError = true, CharSet = CharSet.Unicode)]
    private static extern bool CryptUnprotectData(ref DataBlob dataIn, IntPtr description, ref DataBlob entropy,
        IntPtr reserved, IntPtr prompt, int flags, ref DataBlob dataOut);

    [DllImport("kernel32.dll")]
    private static extern IntPtr LocalFree(IntPtr hMem);
}
