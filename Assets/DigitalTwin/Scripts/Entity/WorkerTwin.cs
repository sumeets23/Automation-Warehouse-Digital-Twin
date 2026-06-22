using UnityEngine;

namespace WarehouseDigitalTwin
{
    public class WorkerTwin : MonoBehaviour
    {
        [Header("Asset Metadata")]
        public string workerID = "W01";
        public string role = "Stocker";
        public string currentTask = "Sorting Items";

        [Header("Simulated Sensors")]
        public string currentZone = "Zone A";
        public float walkingDistance = 0.0f;
        public float heartRate = 72.0f; 
        [Range(0, 100)] public float fatigue = 0.0f; 
        public float productivity = 100.0f; 

        [Header("Simulation Parameters")]
        public float fatigueIncreaseRate = 0.02f; 
        public float fatigueRecoveryRate = 0.1f;  

        private Vector3 lastPosition;
        private float timeElapsed = 0.0f;

        private void Start()
        {
            lastPosition = transform.position;
            
            fatigue = Random.Range(10f, 40f);
            heartRate = Random.Range(70f, 85f);
        }

        private void Update()
        {
            SimulateBiometrics();
        }

        private void SimulateBiometrics()
        {
            
            float distance = Vector3.Distance(transform.position, lastPosition);
            walkingDistance += distance;
            float speed = distance / Time.deltaTime;
            lastPosition = transform.position;

            
            
            if (transform.position.z > 10f)
            {
                currentZone = "Zone A (Receiving)";
            }
            else if (transform.position.z < -10f)
            {
                currentZone = "Zone B (Cold Storage)";
            }
            else if (transform.position.x > 10f)
            {
                currentZone = "Zone C (Loading Docks)";
            }
            else
            {
                currentZone = "Zone D (Main Aisle)";
            }

            
            if (speed > 0.1f)
            {
                currentTask = "Walking / Relocating";
                timeElapsed += Time.deltaTime;

                
                float targetHR = 75f + (speed * 30f);
                heartRate = Mathf.Lerp(heartRate, targetHR, Time.deltaTime * 2.0f);

                
                fatigue += fatigueIncreaseRate * (1.0f + (speed * 0.5f)) * Time.deltaTime;
            }
            else
            {
                currentTask = "Sorting / Inventory Scan";
                heartRate = Mathf.Lerp(heartRate, Random.Range(70f, 75f), Time.deltaTime);

                
                fatigue -= fatigueRecoveryRate * Time.deltaTime;
            }

            fatigue = Mathf.Clamp(fatigue, 0f, 100f);

            
            productivity = 100f - (fatigue * 0.6f);
            
            productivity += Random.Range(-2f, 2f);
            productivity = Mathf.Clamp(productivity, 10f, 100f);
        }

        
    }
}
