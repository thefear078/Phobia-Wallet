using System.Security.Cryptography;
using NBitcoin;
using NBitcoin.Altcoins;
using NBitcoin.DataEncoders;
using Nethereum.Util;
using Umbrella.Wallet.Core.Chains;
using Umbrella.Wallet.Core.Seed;

namespace Umbrella.Wallet.Core.Derivation;

/// <summary>
/// Derives deterministic receive addresses from a BIP39 mnemonic for supported chains.
/// </summary>
public sealed class HdAddressDeriver
{
    private readonly Bip39MnemonicService _mnemonicService;

    public HdAddressDeriver(Bip39MnemonicService? mnemonicService = null)
    {
        _mnemonicService = mnemonicService ?? new Bip39MnemonicService();
    }

    /// <summary>
    /// Derives the external (receive) address at the given index for a supported chain.
    /// </summary>
    public ReceiveAddress DeriveReceiveAddress(string mnemonic, ChainId chain, uint addressIndex = 0)
    {
        var validation = _mnemonicService.Validate(mnemonic);
        if (!validation.IsValid || validation.NormalizedMnemonic is null)
        {
            throw new ArgumentException(validation.Error ?? "Invalid mnemonic.", nameof(mnemonic));
        }

        var info = ChainCatalog.Get(chain);
        if (!ChainCatalog.HasRealAddress(chain))
        {
            throw new UnsupportedChainException(chain);
        }

        var parsed = Bip39MnemonicService.ParseValidated(validation.NormalizedMnemonic);
        var masterKey = parsed.DeriveExtKey();

        return chain switch
        {
            ChainId.Btc => DeriveBitcoinLike(
                masterKey,
                ChainId.Btc,
                Network.Main,
                ScriptPubKeyType.Segwit,
                purpose: 84,
                coinType: 0,
                addressIndex),
            ChainId.Ltc => DeriveBitcoinLike(
                masterKey,
                ChainId.Ltc,
                Litecoin.Instance.Mainnet,
                ScriptPubKeyType.Segwit,
                purpose: 84,
                coinType: 2,
                addressIndex),
            ChainId.Doge => DeriveBitcoinLike(
                masterKey,
                ChainId.Doge,
                Dogecoin.Instance.Mainnet,
                ScriptPubKeyType.Legacy,
                purpose: 44,
                coinType: 3,
                addressIndex),
            ChainId.Eth => DeriveEthereum(masterKey, addressIndex),
            ChainId.Tron => DeriveTron(masterKey, addressIndex),
            ChainId.Sol => DeriveSolana(parsed, addressIndex),
            ChainId.Xmr => DeriveMonero(parsed),
            ChainId.Ton or ChainId.Ada =>
                throw new UnsupportedChainException(chain),
            _ => throw new ArgumentOutOfRangeException(nameof(chain), chain, "Unknown chain id."),
        };
    }

    /// <summary>
    /// Solana: SLIP-0010 ed25519 at m/44'/501'/0'/{index}', base58 of the public key.
    /// Matches Phantom / solana-keygen for the account-0 address.
    /// </summary>
    private static ReceiveAddress DeriveSolana(Mnemonic parsed, uint addressIndex)
    {
        var seed = parsed.DeriveSeed();
        var priv = Slip10Ed25519.DerivePrivateKey(seed, new[] { 44u, 501u, 0u, addressIndex });
        var pub = Slip10Ed25519.PublicKey(priv);
        var address = Encoders.Base58.EncodeData(pub);
        var path = $"44'/501'/0'/{addressIndex}'";
        return new ReceiveAddress(ChainId.Sol, address, "m/" + path, addressIndex);
    }

    private static ReceiveAddress DeriveBitcoinLike(
        ExtKey masterKey,
        ChainId chain,
        Network network,
        ScriptPubKeyType scriptType,
        int purpose,
        int coinType,
        uint addressIndex)
    {
        var path = new KeyPath($"{purpose}'/{coinType}'/0'/0/{addressIndex}");
        var derived = masterKey.Derive(path);
        var address = derived.PrivateKey.PubKey.GetAddress(scriptType, network).ToString();
        return new ReceiveAddress(chain, address, FormatPath(path), addressIndex);
    }

    /// <summary>
    /// Ethereum private key (32 bytes) at m/44'/60'/0'/0/{index}. Used transiently for local
    /// transaction signing only — the caller must zero the array after use.
    /// </summary>
    public byte[] DeriveEthereumPrivateKey(string mnemonic, uint addressIndex = 0)
    {
        var validation = _mnemonicService.Validate(mnemonic);
        if (!validation.IsValid || validation.NormalizedMnemonic is null)
        {
            throw new ArgumentException(validation.Error ?? "Invalid mnemonic.", nameof(mnemonic));
        }

        var parsed = Bip39MnemonicService.ParseValidated(validation.NormalizedMnemonic);
        var derived = parsed.DeriveExtKey().Derive(new KeyPath($"44'/60'/0'/0/{addressIndex}"));
        return derived.PrivateKey.ToBytes();
    }

    /// <summary>
    /// The NBitcoin <see cref="Key"/> behind the displayed BTC/LTC receive address, for local
    /// signing only. Path matches <see cref="DeriveReceiveAddress"/> exactly (BIP84).
    /// </summary>
    public Key DeriveBitcoinLikeKey(string mnemonic, ChainId chain, uint addressIndex = 0)
    {
        var (purpose, coinType) = chain switch
        {
            ChainId.Btc => (84, 0),
            ChainId.Ltc => (84, 2),
            ChainId.Doge => (44, 3),
            _ => throw new UnsupportedChainException(chain),
        };

        var validation = _mnemonicService.Validate(mnemonic);
        if (!validation.IsValid || validation.NormalizedMnemonic is null)
        {
            throw new ArgumentException(validation.Error ?? "Invalid mnemonic.", nameof(mnemonic));
        }

        var parsed = Bip39MnemonicService.ParseValidated(validation.NormalizedMnemonic);
        return parsed.DeriveExtKey()
            .Derive(new KeyPath($"{purpose}'/{coinType}'/0'/0/{addressIndex}"))
            .PrivateKey;
    }

    /// <summary>
    /// TRON signing key at m/44'/195'/0'/0/{index} — same path as the displayed TRX address.
    /// Used for native TRX and USDT (TRC-20) transfers.
    /// </summary>
    public Key DeriveTronKey(string mnemonic, uint addressIndex = 0)
    {
        var validation = _mnemonicService.Validate(mnemonic);
        if (!validation.IsValid || validation.NormalizedMnemonic is null)
        {
            throw new ArgumentException(validation.Error ?? "Invalid mnemonic.", nameof(mnemonic));
        }

        var parsed = Bip39MnemonicService.ParseValidated(validation.NormalizedMnemonic);
        return parsed.DeriveExtKey()
            .Derive(new KeyPath($"44'/195'/0'/0/{addressIndex}"))
            .PrivateKey;
    }

    /// <summary>
    /// The full Monero account (address + secret keys) for this wallet. The secret keys are what
    /// "Restore from keys" consumes in Feather / monero-wallet-cli.
    /// </summary>
    public MoneroWallet DeriveMoneroWallet(string mnemonic)
    {
        var validation = _mnemonicService.Validate(mnemonic);
        if (!validation.IsValid || validation.NormalizedMnemonic is null)
        {
            throw new ArgumentException(validation.Error ?? "Invalid mnemonic.", nameof(mnemonic));
        }

        var parsed = Bip39MnemonicService.ParseValidated(validation.NormalizedMnemonic);
        return MoneroKeys.FromSeed(parsed.DeriveSeed());
    }

    private static ReceiveAddress DeriveMonero(Mnemonic parsed)
    {
        var wallet = MoneroKeys.FromSeed(parsed.DeriveSeed());
        return new ReceiveAddress(ChainId.Xmr, wallet.Address, "umbrella-monero-v1", 0);
    }

    /// <summary>
    /// Solana ed25519 secret scalar (32 bytes) at m/44'/501'/0'/{index}', for local signing only.
    /// </summary>
    public byte[] DeriveSolanaPrivateKey(string mnemonic, uint addressIndex = 0)
    {
        var validation = _mnemonicService.Validate(mnemonic);
        if (!validation.IsValid || validation.NormalizedMnemonic is null)
        {
            throw new ArgumentException(validation.Error ?? "Invalid mnemonic.", nameof(mnemonic));
        }

        var parsed = Bip39MnemonicService.ParseValidated(validation.NormalizedMnemonic);
        return Slip10Ed25519.DerivePrivateKey(parsed.DeriveSeed(), new[] { 44u, 501u, 0u, addressIndex });
    }

    private static ReceiveAddress DeriveEthereum(ExtKey masterKey, uint addressIndex)
    {
        var path = new KeyPath($"44'/60'/0'/0/{addressIndex}");
        var derived = masterKey.Derive(path);
        var addressBytes = GetSecp256k1AddressBytes(derived.PrivateKey.PubKey);
        var hex = "0x" + Encoders.Hex.EncodeData(addressBytes);
        var checksum = AddressUtil.Current.ConvertToChecksumAddress(hex);
        return new ReceiveAddress(ChainId.Eth, checksum, FormatPath(path), addressIndex);
    }

    private static ReceiveAddress DeriveTron(ExtKey masterKey, uint addressIndex)
    {
        var path = new KeyPath($"44'/195'/0'/0/{addressIndex}");
        var derived = masterKey.Derive(path);
        var addressBytes = GetSecp256k1AddressBytes(derived.PrivateKey.PubKey);

        // TRON mainnet: version byte 0x41 + 20-byte address, Base58Check.
        var payload = new byte[21];
        payload[0] = 0x41;
        Buffer.BlockCopy(addressBytes, 0, payload, 1, 20);
        var address = EncodeBase58Check(payload);
        return new ReceiveAddress(ChainId.Tron, address, FormatPath(path), addressIndex);
    }

    private static string FormatPath(KeyPath path) => "m/" + path;

    /// <summary>
    /// Keccak-256 of the uncompressed public key (without 0x04 prefix), last 20 bytes.
    /// Shared by Ethereum and TRON.
    /// </summary>
    private static byte[] GetSecp256k1AddressBytes(PubKey pubKey)
    {
        var uncompressed = pubKey.Decompress().ToBytes();
        if (uncompressed.Length != 65 || uncompressed[0] != 0x04)
        {
            throw new InvalidOperationException("Expected uncompressed secp256k1 public key.");
        }

        var hash = Sha3Keccack.Current.CalculateHash(uncompressed.AsSpan(1).ToArray());
        var address = new byte[20];
        Buffer.BlockCopy(hash, 12, address, 0, 20);
        return address;
    }

    private static string EncodeBase58Check(byte[] payload)
    {
        var checksum = DoubleSha256(payload);
        var data = new byte[payload.Length + 4];
        Buffer.BlockCopy(payload, 0, data, 0, payload.Length);
        Buffer.BlockCopy(checksum, 0, data, payload.Length, 4);
        return Encoders.Base58.EncodeData(data);
    }

    private static byte[] DoubleSha256(byte[] data)
    {
        var first = SHA256.HashData(data);
        return SHA256.HashData(first);
    }
}
