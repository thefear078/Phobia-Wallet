using Umbrella.Wallet.Infrastructure.Network;

namespace Umbrella.Wallet.App.ViewModels;

/// <summary>
/// Private broadcast (roadmap P2.4): with Tor or a proxy on, a Bitcoin, Litecoin or Dogecoin payment
/// is first handed to one random node of its network over a circuit of its own, and the explorers are
/// only asked whether the network carried it — see <see cref="PeerRelay"/>.
/// </summary>
public partial class MainViewModel
{
    public bool PrivateRelayEnabled
    {
        get => _uiSettings.PrivateRelay;
        set
        {
            if (_uiSettings.PrivateRelay == value) return;
            _uiSettings.PrivateRelay = value;
            _uiSettings.Save();
            BitcoinTransactionSender.PrivateRelay = value;
            OnPropertyChanged();
        }
    }
}
