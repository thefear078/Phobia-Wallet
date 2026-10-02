using System.Collections.ObjectModel;
using System.Globalization;
using System.Text;
using CommunityToolkit.Mvvm.ComponentModel;
using CommunityToolkit.Mvvm.Input;

namespace Umbrella.Wallet.App.ViewModels;

/// <summary>
/// The search box at the top of Settings. It searches what the Settings cards actually say — every
/// title, hint and switch, in the wallet's language and in English — plus words people type that the
/// screen does not (seed, тема, proxy…). Picking a result opens its pane and scrolls to the card.
///
/// It used to search seventeen English phrases, so a Ukrainian "тема" or "мова" found nothing.
/// </summary>
public partial class MainViewModel
{
    /// <summary>Every card in Settings — and the settings inside the bigger ones — with the pane it is on
    /// and the strings it shows. Built from the Settings screen itself (a test keeps the two in step).</summary>
    public static readonly IReadOnlyList<SettingsShortcut> SettingsIndex =
    [
        new("settings.walletsTitle", "Wallets", ["settings.walletsTitle", "settings.walletsHint", "settings.walletSwitch", "wallets.removedTitle", "wallets.removedHint", "wallets.restore", "settings.walletRenameWatermark", "settings.walletRename", "settings.walletColor", "common.clear", "found.title", "found.hint", "found.scan", "found.show", "wallets.showTotals", "settings.walletCoins", "settings.walletAddTitle", "settings.walletAddHint", "settings.walletAddWatermark", "settings.walletAdd", "settings.changePwTitle", "settings.changePwHint", "settings.changePwCurrent", "settings.changePwNew", "settings.changePwConfirm", "settings.changePwBtn"], "wallets switch accounts гаманці перемкнути кошельки переключить"),
        new("wallets.removedTitle", "Wallets", ["wallets.removedTitle", "wallets.removedHint"], ""),
        new("settings.walletRename", "Wallets", ["settings.walletRename"], ""),
        new("settings.walletColor", "Wallets", ["settings.walletColor"], ""),
        new("found.title", "Wallets", ["found.title", "found.hint"], "found accounts other apps знайдені рахунки найденные"),
        new("wallets.showTotals", "Wallets", ["wallets.showTotals"], "all balances totals sum усі баланси сума все балансы"),
        new("settings.walletCoins", "Wallets", ["settings.walletCoins"], ""),
        new("settings.walletAddTitle", "Wallets", ["settings.walletAddTitle", "settings.walletAddHint"], "add wallet new import second додати гаманець новий добавить кошелек"),
        new("settings.changePwTitle", "Wallets", ["settings.changePwTitle", "settings.changePwHint"], "password change пароль змінити сменить"),
        new("settings.language", "Appearance", ["settings.language", "settings.languageHint"], "language interface english ukrainian мова язык sprache idioma 语言"),
        new("settings.currency", "Appearance", ["settings.currency", "settings.currencyHint"], "currency fiat usd eur uah cny money валюта гривня долар юань деньги währung moneda 货币"),
        new("settings.theme", "Appearance", ["settings.theme", "settings.themeHint", "settings.qrNote"], "theme colour color dark light тема колір кольори цвет farbe tema 主题 颜色"),
        new("settings.nav", "Appearance", ["settings.nav", "settings.navHint"], "sidebar navigation menu panel бокова панель меню навігація навигация"),
        new("settings.interface", "Appearance", ["settings.interface", "settings.scrollTop", "settings.scrollTopHint", "settings.notesReset", "settings.notesResetHint", "settings.notesResetBtn"], "interface scroll top button notes tips інтерфейс кнопка нагору підказки интерфейс наверх подсказки"),
        new("settings.scrollTop", "Appearance", ["settings.scrollTop", "settings.scrollTopHint"], "scroll top back up button прокрутка нагору кнопка наверх"),
        new("settings.notesReset", "Appearance", ["settings.notesReset", "settings.notesResetHint"], "notes tips hints closed підказки закриті подсказки"),
        new("settings.motion", "Appearance", ["settings.motion", "settings.motionHint", "settings.animOn", "settings.animOff", "settings.stickers", "settings.floating", "settings.glints", "settings.shine", "settings.aurora"], "animation motion effects анімація ефекти анимация эффекты"),
        new("settings.stickers", "Appearance", ["settings.stickers"], ""),
        new("settings.profile", "Appearance", ["settings.profile", "settings.profileHint", "settings.walletNameHint", "settings.sidebarBg", "settings.reset"], "profile avatar banner photo профіль аватар фото профиль"),
        new("settings.lockScreen", "Appearance", ["settings.lockScreen", "settings.lockScreenHint", "settings.lockBgChoose", "settings.lockBgReset", "settings.lockPlain"], "lock screen background wallpaper екран блокування фон шпалери экран блокировки"),
        new("settings.updates", "Appearance", ["settings.updates", "settings.updatesHint", "settings.autoUpdateCheck", "settings.autoUpdateDownload", "settings.checkUpdates", "update.download", "update.install", "update.openFolder", "settings.copyDownload", "update.whatsNew", "settings.updateProvenance"], "update version download release beta оновлення версія оновити обновление"),
        new("settings.encryption", "Security", ["settings.encryption", "settings.vault", "settings.seed", "settings.vaultFile", "settings.openVaultFolder"], "encryption vault file шифрування сховище шифрование хранилище"),
        new("settings.vaultFile", "Security", ["settings.vaultFile"], ""),
        new("settings.autolock", "Security", ["settings.autolock", "settings.autolockHint", "settings.hideBal", "settings.hideBalHint", "settings.hideBalOn", "settings.hideBalOff"], "auto lock idle timer minutes автоблокування таймер блокування автоблокировка"),
        new("settings.hideBal", "Security", ["settings.hideBal", "settings.hideBalHint"], "hide balance privacy приховати баланс сховати скрыть"),
        new("settings.secCenter", "Security", ["settings.secCenter", "settings.secCenterHint", "settings.openSecCenter"], "security center checklist безпека центр безопасность"),
        new("verify.title", "Security", ["verify.title", "verify.hint", "verify.costTitle", "verify.cost", "settings.vaultPassword", "verify.revealBtn", "verify.guideBtn", "common.copy", "settings.hideKeys"], "verify keys self check перевірка ключів проверка"),
        new("psbt.title", "Security", ["psbt.title", "psbt.hint", "psbt.paste", "psbt.loadBtn", "psbt.reviewBtn", "psbt.signBtn", "common.copy", "psbt.saveBtn", "psbt.broadcastBtn"], "psbt offline sign hardware cold підпис офлайн подпись"),
        new("duress.title", "Security", ["duress.title", "duress.hint", "duress.limitsTitle", "duress.limits", "duress.currentPw", "duress.newPw", "duress.confirmPw", "duress.setBtn", "duress.removeBtn", "duress.phraseTitle", "duress.phraseHint", "settings.hidePhrase"], "duress panic decoy fake password примус паніка фальшивий паника"),
        new("duress.phraseTitle", "Security", ["duress.phraseTitle", "duress.phraseHint"], ""),
        new("signmsg.title", "Security", ["signmsg.title", "signmsg.hint", "signmsg.yourAddress", "signmsg.messagePh", "signmsg.signBtn", "signmsg.copySig", "signmsg.verifyTitle", "signmsg.addrPh", "signmsg.sigPh", "signmsg.verifyBtn"], "sign message verify signature підписати повідомлення підпис подписать сообщение"),
        new("settings.privacyNet", "Privacy", ["settings.privacyNet", "settings.priv1", "settings.priv2", "settings.priv3", "settings.endpoints", "settings.chains"], "privacy network приватність мережа приватность сеть"),
        new("settings.tor", "Privacy", ["settings.tor", "settings.torHint", "settings.torOn", "settings.torOff", "priv.torOnly", "priv.torOnlyDesc", "priv.torCheck", "settings.moneroSvc", "settings.moneroSvcHint", "settings.moneroOn", "settings.moneroOff", "xmr.node.title", "xmr.node.hint", "xmr.node.custom", "xmr.node.apply", "xmr.node.customHint", "xmr.node.sees", "xmr.node.seesBody", "xmr.node.cannot", "xmr.node.cannotBody"], "tor onion ip anonymity тор анонімність анонимность"),
        new("priv.torOnly", "Privacy", ["priv.torOnly", "priv.torOnlyDesc"], "tor only kill switch лише tor только"),
        new("settings.moneroSvc", "Privacy", ["settings.moneroSvc", "settings.moneroSvcHint"], "monero xmr service монеро сервіс"),
        new("xmr.node.title", "Privacy", ["xmr.node.title", "xmr.node.hint"], "monero node xmr вузол узел"),
        new("xmr.node.custom", "Privacy", ["xmr.node.custom", "xmr.node.customHint"], ""),
        new("endpoint.title", "Privacy", ["endpoint.title", "endpoint.hint", "endpoint.chain", "endpoint.custom", "endpoint.apply", "endpoint.customHint", "endpoint.reset"], "node rpc server endpoint вузол сервер узел"),
        new("endpoint.custom", "Privacy", ["endpoint.custom", "endpoint.customHint"], ""),
        new("who.title", "Privacy", ["who.title", "who.hint", "who.addressNote"], "who talks counterparties servers хто сервери з ким кто серверы"),
        new("settings.proxy", "Privacy", ["settings.proxy", "settings.proxyHint", "settings.proxyOn", "settings.proxyOff", "settings.proxyApply", "settings.ipMode", "settings.ipModeHint", "settings.clipboard", "settings.clipboardHint", "settings.lockMin", "settings.lockMinHint", "settings.lockMinOn", "settings.lockMinOff", "sec.sendPw", "sec.sendPwOnBody", "sec.on", "sec.off", "settings.richData", "settings.richDataHint", "settings.richOn", "settings.richOff"], "proxy socks5 проксі прокси"),
        new("settings.ipMode", "Privacy", ["settings.ipMode", "settings.ipModeHint"], "ipv4 ipv6 ip"),
        new("settings.clipboard", "Privacy", ["settings.clipboard", "settings.clipboardHint"], "clipboard copy буфер обміну копіювання буфер обмена"),
        new("settings.lockMin", "Privacy", ["settings.lockMin", "settings.lockMinHint"], "minimize lock згорнути блокування свернуть"),
        new("sec.sendPw", "Privacy", ["sec.sendPw", "sec.sendPwOnBody"], "send password confirm пароль надсилання отправка"),
        new("settings.richData", "Privacy", ["settings.richData", "settings.richDataHint"], "market data coingecko ринкові дані рыночные данные"),
        new("addrchk.title", "Privacy", ["addrchk.title", "addrchk.hint", "addrchk.placeholder"], "address check verify scam перевірка адреси проверка адреса"),
        new("about.title", "Guide", ["about.title", "about.licence", "about.licenceBody", "about.verify", "about.repo", "about.releases", "sec.tgChannel", "about.trademark", "about.notices"], "about version licence license publisher про версія ліцензія лицензия"),
        new("settings.documentation", "Guide", ["settings.documentation", "settings.openGuide"], "guide help docs manual документація довідка інструкція документация справка"),
        new("settings.backupFile", "Backup", ["settings.backupFile", "settings.backupHint", "settings.saveBackup", "settings.restoreBackup", "settings.verifyBackup", "settings.verifyBackupPw", "settings.verifyBackupBtn", "settings.pwVsPhrase", "settings.restoreNote"], "backup restore export import file резервна копія відновлення бекап резервная копия"),
        new("settings.recovery", "Backup", ["settings.recovery", "settings.keysIntro", "settings.recoveryHint", "settings.vaultPassword", "settings.revealPhrase", "backup.copyHint", "settings.hidePhrase", "settings.moneroAdvanced", "settings.moneroExportHint", "settings.revealMoneroKeys", "settings.primaryAddress", "settings.spendKey", "settings.viewKey", "settings.hideKeys"], "recovery phrase seed words mnemonic 24 фраза відновлення сід слова мнемоніка восстановления"),
        new("settings.maintenance", "Danger", ["settings.maintenance", "settings.clearHistory", "settings.clearHistoryHint", "settings.clear", "settings.disconnectAll", "settings.disconnectAllHint", "settings.disconnect"], "clear history disconnect історія очистити история"),
        new("settings.clearHistory", "Danger", ["settings.clearHistory", "settings.clearHistoryHint"], ""),
        new("settings.disconnectAll", "Danger", ["settings.disconnectAll", "settings.disconnectAllHint"], ""),
        new("settings.dangerZone", "Danger", ["settings.dangerZone", "settings.dangerHint", "settings.danger1", "settings.danger2", "settings.danger3", "settings.dangerConfirmHint", "settings.deleteVault", "settings.lockNow"], "delete wipe erase reset видалити стерти скинути удалить"),
    ];

    public ObservableCollection<SettingsShortcut> SettingsResults { get; } = [];
    public bool HasSettingsResults => SettingsResults.Count > 0;

    [ObservableProperty] private string _settingsSearch = string.Empty;

    /// <summary>Asked of the view: bring the card titled with this text into sight and light it up.</summary>
    public event Action<string>? SettingsFocusRequested;

    partial void OnSettingsSearchChanged(string value)
    {
        SettingsResults.Clear();
        foreach (var hit in SearchSettings(value)) SettingsResults.Add(hit);
        OnPropertyChanged(nameof(HasSettingsResults));
    }

    /// <summary>The settings that match every word of <paramref name="query"/>, the closest first: a match
    /// in the title before one in a hint. At most twelve.</summary>
    public static IReadOnlyList<SettingsShortcut> SearchSettings(string? query)
    {
        var words = Fold(query).Split(' ', StringSplitOptions.RemoveEmptyEntries);
        if (words.Length == 0) return [];

        return SettingsIndex
            .Select(s => (Shortcut: s, Title: Fold(s.Label + " " + Loc.Instance.InEnglish(s.TitleKey)), Text: Haystack(s)))
            .Where(x => words.All(w => x.Text.Contains(w, StringComparison.Ordinal)))
            .OrderByDescending(x => words.Count(w => x.Title.Contains(w, StringComparison.Ordinal)))
            .ThenBy(x => SettingsIndex.ToList().IndexOf(x.Shortcut))
            .Select(x => x.Shortcut)
            .GroupBy(s => s.Label)          // one row per title, even where two cards share it
            .Select(g => g.First())
            .Take(12)
            .ToList();
    }

    /// <summary>Everything one entry can be found by: its strings now and in English, its pane, its words.</summary>
    private static string Haystack(SettingsShortcut s)
    {
        var text = new StringBuilder();
        foreach (var key in s.Keys) text.Append(Loc.Instance[key]).Append(' ').Append(Loc.Instance.InEnglish(key)).Append(' ');
        text.Append(s.TabLabel).Append(' ').Append(Loc.Instance.InEnglish(s.TabKey)).Append(' ').Append(s.Keywords);
        return Fold(text.ToString());
    }

    /// <summary>Lower case, accents and punctuation away, so "Тема", "тема" and "ТЕМА," all match.</summary>
    public static string Fold(string? text)
    {
        if (string.IsNullOrWhiteSpace(text)) return string.Empty;
        var decomposed = text.ToLowerInvariant().Replace('ё', 'е').Replace('ї', 'і').Normalize(NormalizationForm.FormD);
        var folded = new StringBuilder(decomposed.Length);
        foreach (var ch in decomposed)
        {
            var category = CharUnicodeInfo.GetUnicodeCategory(ch);
            if (category == UnicodeCategory.NonSpacingMark) continue;
            folded.Append(char.IsLetterOrDigit(ch) ? ch : ' ');
        }
        return folded.ToString().Normalize(NormalizationForm.FormC);
    }

    /// <summary>Opens a result: its pane, then the card itself, scrolled into sight.</summary>
    [RelayCommand]
    private void OpenSettingResult(SettingsShortcut? shortcut)
    {
        if (shortcut is null) return;
        SettingsTab = shortcut.Tab;
        SettingsSearch = string.Empty;
        SettingsFocusRequested?.Invoke(Loc.Instance[shortcut.TitleKey]);
    }

    /// <summary>Open a settings pane and clear the query (the ✕ in the box passes no pane).</summary>
    [RelayCommand]
    private void OpenSetting(string? tab)
    {
        if (!string.IsNullOrWhiteSpace(tab)) SettingsTab = tab;
        SettingsSearch = string.Empty;
    }
}
