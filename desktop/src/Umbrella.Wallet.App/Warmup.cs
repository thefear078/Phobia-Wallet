using System.Security.Cryptography;
using Umbrella.Wallet.Core.Chains;
using Umbrella.Wallet.Core.Derivation;

namespace Umbrella.Wallet.App;

/// <summary>
/// Runs the code an unlock needs once, on a throwaway input, while the lock screen waits for the
/// password. The first unlock of a session spent more than a second compiling the key derivation and
/// the address code before running it; done here, it is already compiled when the password arrives.
///
/// Nothing here touches the vault, the network or any real key: the phrase is generated, used and
/// dropped.
/// </summary>
public static class Warmup
{
    private static int _started;

    public static void Start()
    {
        if (Interlocked.Exchange(ref _started, 1) == 1) return;
        var thread = new Thread(Run) { IsBackground = true, Priority = ThreadPriority.BelowNormal, Name = "warmup" };
        thread.Start();
    }

    private static void Run()
    {
        try
        {
            // The vault's KDF, small: the same code paths, a fraction of the memory.
            using (var argon2 = new Konscious.Security.Cryptography.Argon2id(RandomNumberGenerator.GetBytes(16))
                   { Salt = RandomNumberGenerator.GetBytes(16), DegreeOfParallelism = 2, Iterations = 1, MemorySize = 1024 })
            {
                argon2.GetBytes(32);
            }

            using (var aes = new AesGcm(RandomNumberGenerator.GetBytes(32), 16))
            {
                var plain = new byte[16];
                var tag = new byte[16];
                aes.Encrypt(RandomNumberGenerator.GetBytes(12), plain, new byte[16], tag);
            }

            // Every chain's address code, once, for a phrase nobody will ever use.
            var phrase = new NBitcoin.Mnemonic(NBitcoin.Wordlist.English, NBitcoin.WordCount.Twelve).ToString();
            var deriver = new HdAddressDeriver();
            foreach (var chain in ChainCatalog.All)
            {
                if (!ChainCatalog.HasRealAddress(chain.Id)) continue;
                try { deriver.DeriveReceiveAddress(phrase, chain.Id); } catch { /* a chain that needs more: skipped */ }
            }
        }
        catch
        {
            // Only ever a head start; an unlock does all of this itself anyway.
        }
    }
}
