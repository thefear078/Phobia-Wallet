using System;
using System.Collections.Generic;
using System.ComponentModel;

namespace Umbrella.Wallet.App;

/// <summary>
/// Runtime UI translation.
///
/// Bindings use the indexer — <c>{Binding [nav.portfolio], Source={x:Static app:Loc.Instance}}</c> —
/// so raising a change notification for the indexer re-reads every localized string at once and
/// the language switches live, with no restart.
///
/// Anything without a translation falls back to English rather than showing a raw key, so a
/// missing entry degrades to readable text instead of breaking the screen.
/// </summary>
public sealed class Loc : INotifyPropertyChanged
{
    public static Loc Instance { get; } = new();

    public event PropertyChangedEventHandler? PropertyChanged;

    public sealed record Language(string Code, string Name);

    public static IReadOnlyList<Language> Languages { get; } =
    [
        new("en", "English"),
        new("uk", "Українська"),
        new("ru", "Русский"),
        new("zh", "中文"),
        new("es", "Español"),
        new("de", "Deutsch"),
    ];

    private string _code = "en";

    public string CurrentCode
    {
        get => _code;
        set
        {
            if (_code == value || !Strings.ContainsKey(value)) return;
            _code = value;
            // Null property name = "everything changed", which refreshes every bound string.
            PropertyChanged?.Invoke(this, new PropertyChangedEventArgs(null));
        }
    }

    /// <summary>Localized string for a key, falling back to English and then to the key itself.</summary>
    public string this[string key]
    {
        get
        {
            if (Strings.TryGetValue(_code, out var table) && table.TryGetValue(key, out var value))
            {
                return value;
            }

            return Strings["en"].TryGetValue(key, out var english) ? english : key;
        }
    }

    public static string Get(string key) => Instance[key];

    private static readonly Dictionary<string, Dictionary<string, string>> Strings = new()
    {
        ["en"] = new()
        {
            ["nav.portfolio"] = "Portfolio",
            ["nav.receive"] = "Receive",
            ["nav.send"] = "Send",
            ["nav.connect"] = "Connect",
            ["nav.market"] = "Market",
            ["nav.activity"] = "Activity",
            ["nav.settings"] = "Settings",
            ["nav.lock"] = "Lock vault",
            ["common.total"] = "TOTAL BALANCE · USD",
            ["common.holdings"] = "Holdings",
            ["common.copy"] = "Copy",
            ["common.copyAddress"] = "Copy address",
            ["common.close"] = "Close",
            ["common.cancel"] = "Cancel",
            ["common.hide"] = "Hide",
            ["common.show"] = "Show",
            ["common.asset"] = "ASSET",
            ["common.price"] = "PRICE",
            ["common.balance"] = "BALANCE",
            ["common.value"] = "VALUE",
            ["receive.title"] = "Receive",
            ["receive.hint"] = "Pick a coin to show its QR code and address.",
            ["receive.scan"] = "Scan this code or copy the address below",
            ["send.title"] = "Send",
            ["send.review"] = "Review transfer",
            ["send.confirm"] = "Confirm & broadcast",
            ["send.amount"] = "AMOUNT",
            ["send.destination"] = "DESTINATION ADDRESS",
            ["connect.title"] = "Connect external wallets",
            ["connect.link"] = "Link address",
            ["settings.title"] = "Settings",
            ["settings.language"] = "LANGUAGE",
            ["settings.languageHint"] = "Changes the interface language immediately.",
            ["settings.appearance"] = "Appearance",
            ["settings.theme"] = "COLOUR THEME",
            ["settings.themeHint"] = "Changes the look of the whole wallet immediately.",
            ["settings.security"] = "Security",
            ["settings.privacy"] = "Privacy & Tor",
            ["settings.backup"] = "Backup & keys",
            ["settings.guide"] = "Guide",
            ["settings.danger"] = "Danger zone",
            ["market.title"] = "Market",
        },
        ["uk"] = new()
        {
            ["nav.portfolio"] = "Портфель",
            ["nav.receive"] = "Отримати",
            ["nav.send"] = "Надіслати",
            ["nav.connect"] = "Підключити",
            ["nav.market"] = "Ринок",
            ["nav.activity"] = "Активність",
            ["nav.settings"] = "Налаштування",
            ["nav.lock"] = "Заблокувати",
            ["common.total"] = "ЗАГАЛЬНИЙ БАЛАНС · USD",
            ["common.holdings"] = "Активи",
            ["common.copy"] = "Копіювати",
            ["common.copyAddress"] = "Копіювати адресу",
            ["common.close"] = "Закрити",
            ["common.cancel"] = "Скасувати",
            ["common.hide"] = "Сховати",
            ["common.show"] = "Показати",
            ["common.asset"] = "АКТИВ",
            ["common.price"] = "ЦІНА",
            ["common.balance"] = "БАЛАНС",
            ["common.value"] = "ВАРТІСТЬ",
            ["receive.title"] = "Отримати",
            ["receive.hint"] = "Оберіть монету, щоб побачити QR-код і адресу.",
            ["receive.scan"] = "Відскануйте код або скопіюйте адресу нижче",
            ["send.title"] = "Надіслати",
            ["send.review"] = "Переглянути переказ",
            ["send.confirm"] = "Підтвердити й надіслати",
            ["send.amount"] = "СУМА",
            ["send.destination"] = "АДРЕСА ОТРИМУВАЧА",
            ["connect.title"] = "Підключити зовнішні гаманці",
            ["connect.link"] = "Додати адресу",
            ["settings.title"] = "Налаштування",
            ["settings.language"] = "МОВА",
            ["settings.languageHint"] = "Мова інтерфейсу змінюється одразу.",
            ["settings.appearance"] = "Вигляд",
            ["settings.theme"] = "КОЛІРНА ТЕМА",
            ["settings.themeHint"] = "Зовнішній вигляд гаманця змінюється одразу.",
            ["settings.security"] = "Безпека",
            ["settings.privacy"] = "Приватність і Tor",
            ["settings.backup"] = "Резервна копія й ключі",
            ["settings.guide"] = "Довідка",
            ["settings.danger"] = "Небезпечна зона",
            ["market.title"] = "Ринок",
        },
        ["ru"] = new()
        {
            ["nav.portfolio"] = "Портфель",
            ["nav.receive"] = "Получить",
            ["nav.send"] = "Отправить",
            ["nav.connect"] = "Подключить",
            ["nav.market"] = "Рынок",
            ["nav.activity"] = "Активность",
            ["nav.settings"] = "Настройки",
            ["nav.lock"] = "Заблокировать",
            ["common.total"] = "ОБЩИЙ БАЛАНС · USD",
            ["common.holdings"] = "Активы",
            ["common.copy"] = "Копировать",
            ["common.copyAddress"] = "Копировать адрес",
            ["common.close"] = "Закрыть",
            ["common.cancel"] = "Отмена",
            ["common.hide"] = "Скрыть",
            ["common.show"] = "Показать",
            ["common.asset"] = "АКТИВ",
            ["common.price"] = "ЦЕНА",
            ["common.balance"] = "БАЛАНС",
            ["common.value"] = "СТОИМОСТЬ",
            ["receive.title"] = "Получить",
            ["receive.hint"] = "Выберите монету, чтобы увидеть QR-код и адрес.",
            ["receive.scan"] = "Отсканируйте код или скопируйте адрес ниже",
            ["send.title"] = "Отправить",
            ["send.review"] = "Проверить перевод",
            ["send.confirm"] = "Подтвердить и отправить",
            ["send.amount"] = "СУММА",
            ["send.destination"] = "АДРЕС ПОЛУЧАТЕЛЯ",
            ["connect.title"] = "Подключить внешние кошельки",
            ["connect.link"] = "Добавить адрес",
            ["settings.title"] = "Настройки",
            ["settings.language"] = "ЯЗЫК",
            ["settings.languageHint"] = "Язык интерфейса меняется сразу.",
            ["settings.appearance"] = "Вид",
            ["settings.theme"] = "ЦВЕТОВАЯ ТЕМА",
            ["settings.themeHint"] = "Внешний вид кошелька меняется сразу.",
            ["settings.security"] = "Безопасность",
            ["settings.privacy"] = "Приватность и Tor",
            ["settings.backup"] = "Резервная копия и ключи",
            ["settings.guide"] = "Справка",
            ["settings.danger"] = "Опасная зона",
            ["market.title"] = "Рынок",
        },
        ["zh"] = new()
        {
            ["nav.portfolio"] = "资产组合",
            ["nav.receive"] = "接收",
            ["nav.send"] = "发送",
            ["nav.connect"] = "连接",
            ["nav.market"] = "行情",
            ["nav.activity"] = "活动",
            ["nav.settings"] = "设置",
            ["nav.lock"] = "锁定钱包",
            ["common.total"] = "总余额 · 美元",
            ["common.holdings"] = "持仓",
            ["common.copy"] = "复制",
            ["common.copyAddress"] = "复制地址",
            ["common.close"] = "关闭",
            ["common.cancel"] = "取消",
            ["common.hide"] = "隐藏",
            ["common.show"] = "显示",
            ["common.asset"] = "资产",
            ["common.price"] = "价格",
            ["common.balance"] = "余额",
            ["common.value"] = "价值",
            ["receive.title"] = "接收",
            ["receive.hint"] = "选择一种币以显示其二维码和地址。",
            ["receive.scan"] = "扫描此二维码或复制下方地址",
            ["send.title"] = "发送",
            ["send.review"] = "检查转账",
            ["send.confirm"] = "确认并广播",
            ["send.amount"] = "金额",
            ["send.destination"] = "接收地址",
            ["connect.title"] = "连接外部钱包",
            ["connect.link"] = "添加地址",
            ["settings.title"] = "设置",
            ["settings.language"] = "语言",
            ["settings.languageHint"] = "界面语言会立即更改。",
            ["settings.appearance"] = "外观",
            ["settings.theme"] = "配色主题",
            ["settings.themeHint"] = "整个钱包的外观会立即更改。",
            ["settings.security"] = "安全",
            ["settings.privacy"] = "隐私与 Tor",
            ["settings.backup"] = "备份与密钥",
            ["settings.guide"] = "指南",
            ["settings.danger"] = "危险区域",
            ["market.title"] = "行情",
        },
        ["es"] = new()
        {
            ["nav.portfolio"] = "Cartera",
            ["nav.receive"] = "Recibir",
            ["nav.send"] = "Enviar",
            ["nav.connect"] = "Conectar",
            ["nav.market"] = "Mercado",
            ["nav.activity"] = "Actividad",
            ["nav.settings"] = "Ajustes",
            ["nav.lock"] = "Bloquear",
            ["common.total"] = "SALDO TOTAL · USD",
            ["common.holdings"] = "Posiciones",
            ["common.copy"] = "Copiar",
            ["common.copyAddress"] = "Copiar dirección",
            ["common.close"] = "Cerrar",
            ["common.cancel"] = "Cancelar",
            ["common.hide"] = "Ocultar",
            ["common.show"] = "Mostrar",
            ["common.asset"] = "ACTIVO",
            ["common.price"] = "PRECIO",
            ["common.balance"] = "SALDO",
            ["common.value"] = "VALOR",
            ["receive.title"] = "Recibir",
            ["receive.hint"] = "Elige una moneda para ver su código QR y dirección.",
            ["receive.scan"] = "Escanea este código o copia la dirección de abajo",
            ["send.title"] = "Enviar",
            ["send.review"] = "Revisar transferencia",
            ["send.confirm"] = "Confirmar y enviar",
            ["send.amount"] = "IMPORTE",
            ["send.destination"] = "DIRECCIÓN DE DESTINO",
            ["connect.title"] = "Conectar carteras externas",
            ["connect.link"] = "Añadir dirección",
            ["settings.title"] = "Ajustes",
            ["settings.language"] = "IDIOMA",
            ["settings.languageHint"] = "El idioma de la interfaz cambia al instante.",
            ["settings.appearance"] = "Apariencia",
            ["settings.theme"] = "TEMA DE COLOR",
            ["settings.themeHint"] = "Cambia el aspecto de toda la cartera al instante.",
            ["settings.security"] = "Seguridad",
            ["settings.privacy"] = "Privacidad y Tor",
            ["settings.backup"] = "Copia y claves",
            ["settings.guide"] = "Guía",
            ["settings.danger"] = "Zona de peligro",
            ["market.title"] = "Mercado",
        },
        ["de"] = new()
        {
            ["nav.portfolio"] = "Portfolio",
            ["nav.receive"] = "Empfangen",
            ["nav.send"] = "Senden",
            ["nav.connect"] = "Verbinden",
            ["nav.market"] = "Markt",
            ["nav.activity"] = "Aktivität",
            ["nav.settings"] = "Einstellungen",
            ["nav.lock"] = "Sperren",
            ["common.total"] = "GESAMTGUTHABEN · USD",
            ["common.holdings"] = "Bestände",
            ["common.copy"] = "Kopieren",
            ["common.copyAddress"] = "Adresse kopieren",
            ["common.close"] = "Schließen",
            ["common.cancel"] = "Abbrechen",
            ["common.hide"] = "Verbergen",
            ["common.show"] = "Anzeigen",
            ["common.asset"] = "WERT",
            ["common.price"] = "PREIS",
            ["common.balance"] = "GUTHABEN",
            ["common.value"] = "WERT",
            ["receive.title"] = "Empfangen",
            ["receive.hint"] = "Wähle eine Münze, um QR-Code und Adresse zu sehen.",
            ["receive.scan"] = "Scanne diesen Code oder kopiere die Adresse unten",
            ["send.title"] = "Senden",
            ["send.review"] = "Überweisung prüfen",
            ["send.confirm"] = "Bestätigen & senden",
            ["send.amount"] = "BETRAG",
            ["send.destination"] = "ZIELADRESSE",
            ["connect.title"] = "Externe Wallets verbinden",
            ["connect.link"] = "Adresse hinzufügen",
            ["settings.title"] = "Einstellungen",
            ["settings.language"] = "SPRACHE",
            ["settings.languageHint"] = "Die Oberflächensprache ändert sich sofort.",
            ["settings.appearance"] = "Darstellung",
            ["settings.theme"] = "FARBTHEMA",
            ["settings.themeHint"] = "Ändert das Aussehen der gesamten Wallet sofort.",
            ["settings.security"] = "Sicherheit",
            ["settings.privacy"] = "Privatsphäre & Tor",
            ["settings.backup"] = "Backup & Schlüssel",
            ["settings.guide"] = "Anleitung",
            ["settings.danger"] = "Gefahrenzone",
            ["market.title"] = "Markt",
        },
    };
}
