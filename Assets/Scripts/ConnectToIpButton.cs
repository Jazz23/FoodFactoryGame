using FishNet.Managing;
using UnityEngine;

public class ConnectToIpButton : MonoBehaviour
{
    [SerializeField] private Rect _area = new(4f, 190f, 256f, 150f);

    private NetworkManager _networkManager;
    private string _clientAddress = "localhost";

    private void Start()
    {
        _networkManager = GetComponent<NetworkManager>();
        if (_networkManager == null)
            _networkManager = FindFirstObjectByType<NetworkManager>();

        if (_networkManager?.TransportManager?.Transport != null)
        {
            string configuredAddress = _networkManager.TransportManager.Transport.GetClientAddress();
            if (!string.IsNullOrEmpty(configuredAddress))
                _clientAddress = configuredAddress;
        }
    }

    private void OnGUI()
    {
        if (_networkManager?.TransportManager?.Transport == null)
            return;

        Matrix4x4 previousMatrix = GUI.matrix;
        GUI.matrix = Matrix4x4.TRS(
            Vector3.zero,
            Quaternion.identity,
            new Vector3(Screen.width / 1920f, Screen.height / 1080f, 1f));

        GUILayout.BeginArea(_area);
        GUIStyle buttonStyle = GUI.skin.GetStyle("button");
        int previousFontSize = buttonStyle.fontSize;
        buttonStyle.fontSize = 26;

        GUILayout.Label("Server IP");
        _clientAddress = GUILayout.TextField(_clientAddress, GUILayout.Width(165f), GUILayout.Height(42f));
        if (GUILayout.Button("Connect to IP", GUILayout.Width(165f), GUILayout.Height(42f)))
            Connect();

        buttonStyle.fontSize = previousFontSize;
        GUILayout.EndArea();
        GUI.matrix = previousMatrix;
    }

    private void Connect()
    {
        string address = _clientAddress.Trim();
        if (string.IsNullOrEmpty(address) || _networkManager.ClientManager == null || _networkManager.ClientManager.Started)
            return;

        _networkManager.TransportManager.Transport.SetClientAddress(address);
        _networkManager.ClientManager.StartConnection();
    }
}
