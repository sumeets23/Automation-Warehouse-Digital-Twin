using UnityEngine;

namespace WarehouseDigitalTwin
{
    public class RackTwin : MonoBehaviour
    {
        [Header("Asset Metadata")]
        public string rackID = "Rack-A01";
        public int capacity = 50;
        public int occupiedSlots = 15;

        private void Start()
        {
            
            occupiedSlots = Random.Range(5, capacity);
        }

        
        public void UpdateInventory(int change)
        {
            occupiedSlots += change;
            occupiedSlots = Mathf.Clamp(occupiedSlots, 0, capacity);
        }

        
    }
}
