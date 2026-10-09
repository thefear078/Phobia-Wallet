using System.Globalization;
using System.Security.Cryptography;
using System.Text;
using System.Text.Json;
using Umbrella.Wallet.Core.Chains;
using Umbrella.Wallet.Core.Derivation;
using Umbrella.Wallet.Core.Seed;

namespace Umbrella.Wallet.Core.Tests;

/// <summary>
/// Monero's own 25-word seed, in every language Monero ships. The keys and addresses below come from
/// monero-python (an independent implementation), and the wordlists are pinned to Monero's C++ source —
/// so a seed written down from the Monero GUI, Feather, Cake Wallet or MyMonero opens the same account here.
/// </summary>
public sealed class MoneroMnemonicTests
{
    // spend key, view key and address from monero-python's test_seed.py, one seed per language.
    [Theory]
    [InlineData("English", "adjust mugged vaults atlas nasty mews damp toenail suddenly toxic possible framed succeed fuzzy return demonstrate nucleus album noises peculiar virtual rowboat inorganic jester fuzzy",
        "482700617ba810f94035d7f4d7ccc1a29878e165b4867872b705204c85406906", "09ed72c713d3e9e19bef2f5204cf85f6cb25de7842aa0722abeb12697f171903",
        "44cWztNFdAqNnycvZbUoj44vsbAEmKnx9aNgkjHdjtMsBrSeKiY8J4s2raH7EMawA2Fwo9utaRTV7Aw8EcTMNMxhH4YtKdH")]
    [InlineData("Chinese (simplified)", "遭 牲 本 点 司 司 仲 吉 虎 只 绝 生 指 纯 伟 破 夫 惊 群 楚 祥 旋 暗 骨 伟",
        "2ec46011b23b0c00468946f1d9a64995bf0a89f9ee0bbf4f64058a3acd81a70e", "aa141796baa24539583306300b44a72495bb7823a0cc6ad856de6d372288d10f",
        "468Dewci4TPfs7TATZ2nf4F1mKAEMp6RraG37wiSU4uT5nAbBwGz5LaB9GWHG23o6ANFJ1Q9cBYk5dRqWNNkmFN4Qx3RqBD")]
    [InlineData("Dutch", "ralf tolvrij copier roon ossuarium wedstrijd splijt debbie bomtapijt occlusie oester noren hiaat scenario geshockt veeteler rotten symboliek jarig bock yoghurt plegen weert zeeblauw wedstrijd",
        "600d3c5022e1844dd2df02f178a074fc2e566793e99d9e1465926adcbfa9b508", "bb8984647124dafcb8682f1c257b5232bb12b96d682bfc320b4f8ce935e2d303",
        "4A5uCL4cXoB9XD3WjTrEwvNBQ6JRPTHaY9uVaxfWmcLy5YkE81tW7B28oc42XGzAeRJkhyHjKAxSE84aZnihjVBVCQf15mw")]
    [InlineData("Esperanto", "knedi aspekti boli asbesto pterido aparta muro sandalo hufumo porcelana degeli utopia ebono lifto dutaga hundo vejno ebono higieno nikotino orkestro arlekeno insekto jaguaro hundo",
        "a8e8a30d3638cc4d09d1fa9f4de12ac0096c69a77896774793627c0cc6a28703", "8b4dcbcbafaf3d195af5bd54aa386d767a8de3b45236c9842cb876212427f103",
        "43YjCQcHm8TAY2kKbSMHz6J8FDZQwjPxw1Cq1vQ7SsQVBNeYEUMwGTQHppi5ffwg3df2m56DYexj2hm5uaQDtqpTBnUVzmD")]
    [InlineData("French", "sauce exprimer chasse asile larve tacler digestion muguet rondeur sept clore narrer fluor arme torse dans glace tant salon sanguin globe quiche ficher flaque clore",
        "597703dd73d0da6b3996b83c3e1e2f602be4f0de453e15846171aa9076901603", "f6e448dbbeaa7682a541b3b5b7e2e8ebb614fac032f1c3dff659ca26ab430f09",
        "42FpfU7DfLi86RtY3ajKUKdrnKvXTx41WPPx6wsyp9XVPcfnrLDXxhucSphpzt3mDv4F1DMiCrfHmR5WPZq1erzn5bs4eA7")]
    [InlineData("German", "Erdgas Gesuch beeilen Chiffon Abendrot Alter Helium Salz Almweide Ampel Dichter Rotglut Dialekt Akkord Rampe Gesöff Ziege Boykott keuchen Krach Anbau Labor Esel Ferien Ampel",
        "193152abe15c5e0a0ff56e3020229398769cd7c6ca5a4e30e439d6702c4f320a", "cdb967c501195827d78a791e1173d4b8826a5ae73b0885984898c84b6c9dd80c",
        "43Z2BHsCkU68NmZrxzfZuuXUtUHCXWttt8MdcnNyDMkC3WmfoFb9byqYjpeBaC4Xtx2dUUv8YPv1d1U4krZCLzyWLUFif2E")]
    [InlineData("Italian", "tramonto spuntare ruota afrodite binocolo riferire moneta assalire tuta firmare malattia flagello paradiso tacere sindrome spuntare sogliola volare follia versare insulto diagnosi lapide meteo malattia",
        "29c8d9e91c1cb59e059bddd901e011db85f8d4f00f967226ffb5e185bd10e70d", "1f224a0330ee358428fe91fa48b6986941030c34f2d1efecc4eb26ea9f838b02",
        "42QQUPDR9PoBrSc9rB5VvG9Wf7KmtjXhEVnLhGKif9rDXGK3n1e6rsVFsh62YDqDf5buVQXuL6oLHGSHg4ANgQUu8beDd9R")]
    [InlineData("Japanese", "いもり すあな いきる しちょう うったえる ちひょう けなみ たいちょう うぶごえ しかい しなぎれ いっせい つかれる しなん ばあさん たいまつばな しひょう おいかける あんがい ていへん せんもん きこく せんく そそぐ つかれる",
        "a047598095d2ada065af73758f7082900b9b0d721b5f99a541a78bd461ffc607", "080c6135edf93233176d41c8535caef0f13d596dc5093b5a5afa4279339dbc00",
        "46hHs9s3boi1NZJHGSwMgfMFLpCBaKwdQQSSf7fqVjWdCDxudsDmqqbKgBkpYDX6JA6MMZG8o5yrMPg9ztrXHdEkSfUA131")]
    [InlineData("Portuguese", "rebuscar mefistofelico luto isca vulva ontologico autuar epiteto jarro invulneravel inquisitorial vietnamita voile potro mamute giroscopio scherzo cheroqui gueto loquaz fissurar fazer violoncelo viquingue vulva",
        "60916cfcb10fa0b2b0648e36ecd7037f5c1972d36b2e6d56c2f4feca613a4200", "b23941e3f4da76e0fab171d94a36fe70031fb501f1f80e0cb3b4b4638b5f7106",
        "43bWUqKAoYWNAdMtuaSF2pY2yptw7zfCB5fV2fXLkYTvj1NNYUKM4aaZtJCVYJunHuD5SNE2CPTCo81wDhZc8bReBidbX1w")]
    [InlineData("Russian", "дощатый ателье мыло паек азот ружье домашний уныние уплата торговля шкаф кекс газета тревога улица армия лазерный иголка друг хищник пашня дневник кричать лыжный иголка",
        "6dc31f6ebcf834ab375a69006cb19c66fcccfa0732dfb3ea1b0662b455226b0d", "5467825ef0148a11582115f80b01c9af90fe31216a9cf6fb2d6b3c78698ce80a",
        "42qVnaWnHSGTERsT6diSvdBTNbHfQZauSfPxpc5EuHc2jK699E28uwpUCRrHr9aaZ4NNyJ9ABdxX6hQHPHv2YcW55A26UbQ")]
    [InlineData("Spanish", "riesgo lápiz martes fuerza dinero pupila pago mensaje guion libro órgano juntar imperio puñal historia pasión nación posible paso límite don afirmar receta reposo fuerza",
        "5973d91299466a9a51ddfcd20d1710c776aa1399279b292b264ab6b7ab608105", "5f7a66cf32120515870f89e3a156ec2024154334a3b43af1da05244ec4cf250d",
        "448MxehQwbgcJyJ3fKnTYYhuF7g7cs7AJdTXoybMu8UEiPFtFpEVNTaDbsK5vatPHVjWwjvJfyWKiM2pBKXJrg4U5qeGXjZ")]
    public void Every_language_opens_the_same_account_as_an_independent_implementation(
        string language, string phrase, string spend, string view, string address)
    {
        Assert.True(MoneroMnemonic.TryDecode(phrase, out var seed, out var problem), problem.ToString());

        Assert.Equal(language, seed!.Language);
        Assert.Equal(spend, seed.Wallet.SecretSpendKeyHex);
        Assert.Equal(view, seed.Wallet.SecretViewKeyHex);
        Assert.Equal(address, seed.Wallet.Address);
        Assert.Equal(phrase, seed.Normalized);   // written out exactly as Monero spells the words
    }

    [Fact]
    public void Chinese_written_without_spaces_is_the_same_seed()
    {
        // The bug report: a Chinese Monero seed was not accepted. People copy it as one run of characters.
        const string spaced = "遭 牲 本 点 司 司 仲 吉 虎 只 绝 生 指 纯 伟 破 夫 惊 群 楚 祥 旋 暗 骨 伟";
        var joined = spaced.Replace(" ", "");

        Assert.True(MoneroMnemonic.TryDecode(joined, out var seed, out _));
        Assert.Equal("468Dewci4TPfs7TATZ2nf4F1mKAEMp6RraG37wiSU4uT5nAbBwGz5LaB9GWHG23o6ANFJ1Q9cBYk5dRqWNNkmFN4Qx3RqBD",
            seed!.Wallet.Address);
        Assert.Equal(spaced, seed.Normalized);

        // Ideographic spaces, full-width commas and numbering are just separators too.
        var numbered = string.Join("，", spaced.Split(' ').Select((w, i) => $"{i + 1}.{w}"));
        Assert.True(MoneroMnemonic.IsMoneroMnemonic(numbered));
        Assert.True(MoneroMnemonic.IsMoneroMnemonic(spaced.Replace(' ', '　')));
    }

    [Fact]
    public void Words_are_found_the_way_Monero_finds_them()
    {
        const string english = "adjust mugged vaults atlas nasty mews damp toenail suddenly toxic possible framed succeed fuzzy return demonstrate nucleus album noises peculiar virtual rowboat inorganic jester fuzzy";
        const string expected = "44cWztNFdAqNnycvZbUoj44vsbAEmKnx9aNgkjHdjtMsBrSeKiY8J4s2raH7EMawA2Fwo9utaRTV7Aw8EcTMNMxhH4YtKdH";

        // Monero identifies an English word by its first three letters, so the short form is the same seed.
        var abbreviated = string.Join(' ', english.Split(' ').Select(w => w[..3]));
        Assert.Equal(expected, MoneroMnemonic.Decode(abbreviated).Wallet.Address);

        // Case, line breaks and "1." numbering from a printed backup don't matter.
        var messy = string.Join("\n", english.ToUpperInvariant().Split(' ').Select((w, i) => $"{i + 1}. {w},"));
        var decoded = MoneroMnemonic.Decode(messy);
        Assert.Equal(expected, decoded.Wallet.Address);
        Assert.Equal(english, decoded.Normalized);   // stored as the full, proper words
    }

    [Fact]
    public void A_Spanish_word_typed_without_its_accent_is_still_found()
    {
        const string phrase = "riesgo lápiz martes fuerza dinero pupila pago mensaje guion libro órgano juntar imperio puñal historia pasión nación posible paso límite don afirmar receta reposo fuerza";
        var plain = new string(phrase.Normalize(NormalizationForm.FormD)
            .Where(c => CharUnicodeInfo.GetUnicodeCategory(c) != UnicodeCategory.NonSpacingMark).ToArray());
        Assert.NotEqual(phrase, plain);

        var seed = MoneroMnemonic.Decode(plain);
        Assert.Equal("448MxehQwbgcJyJ3fKnTYYhuF7g7cs7AJdTXoybMu8UEiPFtFpEVNTaDbsK5vatPHVjWwjvJfyWKiM2pBKXJrg4U5qeGXjZ",
            seed.Wallet.Address);
        Assert.Equal(phrase, seed.Normalized);
    }

    [Fact]
    public void A_mistyped_word_is_caught_by_the_checksum()
    {
        var words = "adjust mugged vaults atlas nasty mews damp toenail suddenly toxic possible framed succeed fuzzy return demonstrate nucleus album noises peculiar virtual rowboat inorganic jester fuzzy".Split(' ');
        words[3] = "abbey";   // a real Monero word — just the wrong one

        Assert.False(MoneroMnemonic.TryDecode(string.Join(' ', words), out var seed, out var problem));
        Assert.Null(seed);
        Assert.Equal(MoneroSeedProblem.Checksum, problem);
    }

    [Theory]
    [InlineData("abandon abandon abandon abandon abandon abandon abandon abandon abandon abandon abandon about", MoneroSeedProblem.WordCount)]
    [InlineData("adjust mugged vaults atlas nasty mews damp toenail suddenly toxic possible framed succeed fuzzy return demonstrate nucleus album noises peculiar virtual rowboat inorganic jester", MoneroSeedProblem.WordCount)]
    [InlineData("adjust mugged vaults atlas nasty mews damp toenail suddenly toxic possible framed succeed fuzzy return demonstrate nucleus album noises peculiar virtual rowboat inorganic qqqqqq fuzzy", MoneroSeedProblem.UnknownWords)]
    [InlineData("", MoneroSeedProblem.WordCount)]
    [InlineData(null, MoneroSeedProblem.WordCount)]
    public void What_is_not_a_Monero_seed_says_why(string? phrase, MoneroSeedProblem expected)
    {
        Assert.Equal(expected, MoneroMnemonic.Check(phrase));
        Assert.False(MoneroMnemonic.IsMoneroMnemonic(phrase));
    }

    [Fact]
    public void A_bip39_phrase_is_never_read_as_a_Monero_seed()
    {
        // 24 English BIP39 words: the wrong length for Monero, so the multi-coin import keeps it.
        const string bip39 = "abandon abandon abandon abandon abandon abandon abandon abandon abandon abandon abandon abandon " +
                             "abandon abandon abandon abandon abandon abandon abandon abandon abandon abandon abandon art";
        Assert.False(MoneroMnemonic.IsMoneroMnemonic(bip39));
    }

    [Fact]
    public void The_deriver_opens_an_imported_Monero_seed_as_that_account()
    {
        var wallet = new HdAddressDeriver().DeriveMoneroWallet("遭牲本点司司仲吉虎只绝生指纯伟破夫惊群楚祥旋暗骨伟");
        Assert.Equal("2ec46011b23b0c00468946f1d9a64995bf0a89f9ee0bbf4f64058a3acd81a70e", wallet.SecretSpendKeyHex);
        Assert.Equal("468Dewci4TPfs7TATZ2nf4F1mKAEMp6RraG37wiSU4uT5nAbBwGz5LaB9GWHG23o6ANFJ1Q9cBYk5dRqWNNkmFN4Qx3RqBD", wallet.Address);
    }

    // SHA-256 of each list's words joined by "\n", read straight from monero-project/monero src/mnemonics/*.h.
    [Theory]
    [InlineData("Chinese (simplified)", 1, "a59c728abd8481ca0b8745e68630ec6a9209304efcef9f5530a1c82cb529f67c")]
    [InlineData("English", 3, "998df55cb16d2318130c5cf7e9d4408247c8c4674935ee6e07d53f2f00ccf19b")]
    [InlineData("Dutch", 4, "2ffcdd53f50a125f2a8c15f6a43438a471821289b61db594bd814982640334e7")]
    [InlineData("French", 4, "2a55ea07dfe12068a3f6b83751ca299e468a6fe875686855094eef226d3ea37f")]
    [InlineData("Spanish", 4, "a9cdd2935fd52fd2c65a3c943ebcf26f1f69d969e13ab2b763001b34dfc02e18")]
    [InlineData("German", 4, "075b4ffbabe753e2d58206df7812afa4df03ab8b871381ce1c4f17204d6bdb82")]
    [InlineData("Italian", 4, "4c580dd95afb208806618b4427454dd266fecef6682ef1aefef4df0a2b5471f2")]
    [InlineData("Portuguese", 4, "ec5b2cf062f3f2185a1caec7508a89dcba59b6f57e5244e5d9d38d53a80ba910")]
    [InlineData("Japanese", 3, "792fbb8305d73a3ac7a3cffcd98cb81e31baefcc469f98c1434cb3f62ecf83b4")]
    [InlineData("Russian", 4, "79debc65deb57dba8b8cc2d9e39c989cc9f24603e30ca79d68b63e4f8ea29c91")]
    [InlineData("Esperanto", 4, "c63babcbf7ad9bee7864470dbd0692bce93b66926ee9f2c932363c1cbc8e7597")]
    [InlineData("Lojban", 4, "b754cdcf4d43b3d74f0718c2248c4bdaf53d43c9fde43b8c6597e83936e7c83b")]
    public void The_wordlists_are_Moneros_own(string language, int prefix, string sha256)
    {
        var words = MoneroMnemonic.WordsOf(language);
        Assert.Equal(1626, words.Count);
        Assert.Equal(prefix, MoneroMnemonic.PrefixOf(language));
        Assert.Equal(sha256, Convert.ToHexString(SHA256.HashData(Encoding.UTF8.GetBytes(string.Join('\n', words)))).ToLowerInvariant());
    }

    [Fact]
    public void Twelve_languages_in_Moneros_search_order()
    {
        Assert.Equal(
            ["Chinese (simplified)", "English", "Dutch", "French", "Spanish", "German", "Italian", "Portuguese",
             "Japanese", "Russian", "Esperanto", "Lojban"],
            MoneroMnemonic.Languages);
    }

    [Fact]
    public void Crc32_is_the_standard_one()
    {
        Assert.Equal(0xCBF43926u, MoneroMnemonic.Crc32("123456789"u8));   // the CRC-32 check value
        Assert.Equal(0u, MoneroMnemonic.Crc32([]));
    }

    /// <summary>
    /// Every language, including Lojban (no published vector): random keys written out the way Monero's
    /// bytes_to_words does, then read back. Covers every word position and the checksum in each list.
    /// </summary>
    [Fact]
    public void Random_keys_round_trip_in_every_language()
    {
        var rng = new Random(20260930);
        foreach (var language in MoneroMnemonic.Languages)
        {
            for (var round = 0; round < 40; round++)
            {
                var key = new byte[32];
                rng.NextBytes(key);
                var phrase = Encode(key, language);

                Assert.True(MoneroMnemonic.TryDecode(phrase, out var seed, out var problem), $"{language}: {problem}");
                Assert.Equal(language, seed!.Language);
                Assert.Equal(phrase, seed.Normalized);
                Assert.Equal(Convert.ToHexString(MoneroKeys.ScReduce32(key)).ToLowerInvariant(), seed.Wallet.SecretSpendKeyHex);
                Assert.Equal(
                    Convert.ToHexString(MoneroKeys.ScReduce32(MoneroKeys.Keccak256(key))).ToLowerInvariant(),
                    seed.Wallet.SecretViewKeyHex);
            }
        }
    }

    /// <summary>Monero's bytes_to_words plus its checksum word, written from electrum-words.cpp.</summary>
    private static string Encode(byte[] key, string language)
    {
        var list = MoneroMnemonic.WordsOf(language);
        var prefix = MoneroMnemonic.PrefixOf(language);
        var n = (uint)list.Count;
        var words = new List<string>();
        for (var i = 0; i < 8; i++)
        {
            var val = BitConverter.ToUInt32(key, i * 4);
            var w1 = val % n;
            var w2 = (val / n + w1) % n;
            var w3 = (val / n / n + w2) % n;
            words.Add(list[(int)w1]);
            words.Add(list[(int)w2]);
            words.Add(list[(int)w3]);
        }

        static string Head(string w, int count) => string.Concat(w.EnumerateRunes().Take(count).Select(r => r.ToString()));
        var trimmed = string.Concat(words.Select(w => Head(w, prefix)));
        words.Add(words[(int)(MoneroMnemonic.Crc32(Encoding.UTF8.GetBytes(trimmed)) % 24)]);
        return string.Join(' ', words);
    }
}

public sealed class MoneroRestoreHeightInputTests
{
    private static readonly DateTimeOffset Now = new(2026, 9, 30, 12, 0, 0, TimeSpan.Zero);

    [Fact]
    public void Nothing_typed_scans_everything()
    {
        Assert.True(MoneroRestoreHeight.TryParse("  ", Now, out var height));
        Assert.Equal(0UL, height);
    }

    [Theory]
    [InlineData("3200000", 3_200_000UL)]
    [InlineData("3 200 000", 3_200_000UL)]
    [InlineData("3,200,000", 3_200_000UL)]
    [InlineData("0", 0UL)]
    public void A_block_height_is_taken_as_written(string input, ulong expected)
    {
        Assert.True(MoneroRestoreHeight.TryParse(input, Now, out var height));
        Assert.Equal(expected, height);
    }

    [Theory]
    [InlineData("2024-05-01")]
    [InlineData("2024/05/01")]
    [InlineData("01.05.2024")]
    public void A_date_becomes_a_height_a_week_before_it(string input)
    {
        Assert.True(MoneroRestoreHeight.TryParse(input, Now, out var height));
        Assert.Equal(MoneroRestoreHeight.ForDate(new DateTimeOffset(2024, 5, 1, 0, 0, 0, TimeSpan.Zero)), height);

        // Blocks 3,100,000 (2024-03-07) and 3,200,000 (2024-07-25) bracket May 1st; 2024-05-01 is ~55 days
        // after the first, so the block that day is about 3,139,600 — and the scan starts a week earlier.
        Assert.InRange(height, 3_130_000UL, 3_136_000UL);
    }

    [Fact]
    public void Anchored_dates_land_before_their_block()
    {
        // Every anchor's own date must map to a height at or below that anchor: a rough date never skips.
        foreach (var (anchor, time) in MoneroRestoreHeight.KnownBlocks)
        {
            Assert.True(MoneroRestoreHeight.ForDate(DateTimeOffset.FromUnixTimeSeconds(time)) <= anchor);
        }
        Assert.Equal(0UL, MoneroRestoreHeight.ForDate(new DateTimeOffset(2014, 1, 1, 0, 0, 0, TimeSpan.Zero)));
    }

    [Theory]
    [InlineData("2027-01-01")]      // in the future
    [InlineData("99999999")]        // past the chain's tip
    [InlineData("three million")]
    [InlineData("-5")]
    [InlineData("3.2e6")]
    public void Anything_else_is_refused(string input)
    {
        Assert.False(MoneroRestoreHeight.TryParse(input, Now, out var height));
        Assert.Equal(0UL, height);
    }
}

/// <summary>The block anchors, re-read from a public node. <c>Category=Live</c>: run by hand.</summary>
[Trait("Category", "Live")]
[Collection(LiveNetworkCollection.Name)]
public sealed class MoneroRestoreHeightAnchorLiveTests
{
    [Fact]
    public async Task Every_anchor_matches_a_real_node()
    {
        Umbrella.Wallet.Infrastructure.Network.PublicHttp.SetProxy(null);
        Umbrella.Wallet.Infrastructure.Network.PublicHttp.SetRequireProxy(false);
        using var http = new HttpClient { Timeout = TimeSpan.FromSeconds(30) };
        foreach (var (height, time) in MoneroRestoreHeight.KnownBlocks)
        {
            using var body = Umbrella.Wallet.Infrastructure.Network.MoneroRpcService.RequestBody(
                "get_block_header_by_height", new { height });
            using var res = await http.PostAsync("https://monero.stackwallet.com:18081/json_rpc", body);
            using var doc = JsonDocument.Parse(await res.Content.ReadAsStringAsync());
            Assert.Equal(time, doc.RootElement.GetProperty("result").GetProperty("block_header").GetProperty("timestamp").GetInt64());
        }
    }
}
