using FishNet.Object;
using FishNet.Object.Synchronizing;

namespace NotAI
{
    public class NAIInventory : NetworkBehaviour
    {
        public readonly SyncVar<int> Items = new(new SyncTypeSettings(WritePermission.ClientUnsynchronized));
        
        
    }
}