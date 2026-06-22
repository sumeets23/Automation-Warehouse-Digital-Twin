using UnityEngine;

namespace WarehouseDigitalTwin
{
    public class Cardbox : MonoBehaviour
    {
        [Header("Cardbox Metadata")]
        [Tooltip("Unique Identifier for this Cardboard Box package.")]
        public string cardboxID;

        [Tooltip("Is this cardbox currently palletized/stacked?")]
        public bool isPalletized;

        [Tooltip("Has this cardbox been shipped to the destination conveyor?")]
        public bool shipped = false;

        [Tooltip("Is this cardbox currently assigned for pickup (or its pallet is being picked up)?")]
        public bool pickupAssigned = false;

        [Tooltip("The shelf structure this cardbox is stored in.")]
        public Transform assignedShelf;

        [Tooltip("Rack ID assigned by the TwinWare editor tool based on cardbox/rack collider overlap.")]
        public string assignedRackId = "None";

        [Header("Conveyor Movement Settings")]
        [Tooltip("Is the pallet currently moving on a conveyor belt?")]
        public bool isOnConveyor = false;

        [Tooltip("Speed of the conveyor belt movement (m/s).")]
        public float conveyorSpeed = 1.0f;

        [Tooltip("How far (meters) the pallet should travel on the conveyor before getting destroyed.")]
        public float maxConveyorDistance = 10.0f;

        private float currentDistanceMoved = 0f;
        private Vector3 lastLocalPosition;

        public void StartConveyorMovement(float speed)
        {
            isOnConveyor = true;
            conveyorSpeed = speed;
            currentDistanceMoved = 0f;

            Transform targetToMove = transform.parent != null && transform.parent.name.StartsWith("CombinedStack_") 
                ? transform.parent 
                : transform;
            lastLocalPosition = targetToMove.localPosition;
        }

        private void Update()
        {
            if (isOnConveyor)
            {
                Transform targetToMove = transform.parent != null && transform.parent.name.StartsWith("CombinedStack_") 
                    ? transform.parent 
                    : transform;

                
                
                Cardbox firstCardbox = targetToMove.GetComponentInChildren<Cardbox>();
                if (firstCardbox != this)
                {
                    isOnConveyor = false;
                    return;
                }

                
                targetToMove.localPosition += Vector3.right * conveyorSpeed * Time.deltaTime;

                
                float stepDist = Vector3.Distance(targetToMove.localPosition, lastLocalPosition);
                currentDistanceMoved += stepDist;
                lastLocalPosition = targetToMove.localPosition;

                
                if (currentDistanceMoved >= maxConveyorDistance)
                {
                    Debug.Log($"[Cardbox] Reached conveyor limit of {maxConveyorDistance}m. Destroying target '{targetToMove.name}'.");
                    Destroy(targetToMove.gameObject);
                }
            }
        }
    }
}
