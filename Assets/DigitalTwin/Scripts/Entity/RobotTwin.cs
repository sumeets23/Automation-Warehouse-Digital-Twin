using System.Collections;
using UnityEngine;

namespace WarehouseDigitalTwin
{
    public class RobotTwin : MonoBehaviour
    {
        [Header("Asset Metadata")]
        public string robotID = "R01";
        public string robotType = "PalletRobot";
        public float loadWeight = 50.0f; 
        public string currentTask = "Available at Home";
        public string currentTargetCardboxID = "None";
        public string status = "Nominal";

        [Header("Simulated Sensors")]
        [Range(0, 100)] public float battery = 100.0f;
        public float temperature = 30.0f; 
        public float speed = 0.0f;

        [Header("Simulation Parameters")]
        public float assignedSpeed = 3.5f; 
        public float batteryConsumptionRate = 0.05f; 
        public float heatGenerationFactor = 2.5f;     
        public float ambientTemperature = 25.0f;       
        public float coolDownRate = 0.2f;              

        [Header("Charging Stations")]
        public Transform chargingStation;
        public Transform chargingWaitingPoint;

        [Header("AI Telemetry & Predictive Maintenance")]
        public float healthScore = 100.0f;
        public float maintenanceRecommendedDays = 30.0f;
        public float etaSeconds = 0.0f;
        public string delayRiskLevel = "Low";
        public float delayConfidence = 100.0f;
        public float anomalyScore = 0.0f;
        public float failureRisk = 0.0f;
        public float rul = 120.0f;

        
        private Vector3 lastPosition;
        private bool isOverheated = false;
        private bool isBatteryFault = false;
        private float stressAccumulator = 0.0f;

        private void Start()
        {
            lastPosition = transform.position;
            
            battery = Random.Range(60f, 100f);
            temperature = Random.Range(26f, 35f);
        }

        private void Update()
        {
            SimulatePhysicsAndSensors();
        }

        private void SimulatePhysicsAndSensors()
        {
            
            float distance = Vector3.Distance(transform.position, lastPosition);
            speed = distance / Time.deltaTime;
            lastPosition = transform.position;

            
            if (GetComponent<RobotPathfinder>() == null)
            {
                if (speed > 0.05f)
                {
                    if (currentTask == "Available at Home" || currentTask == "Charging")
                    {
                        currentTask = "Transporting Cargo";
                    }
                }
                else
                {
                    if (battery < 95f && chargingStation != null && Vector3.Distance(transform.position, chargingStation.position) < 3.0f)
                    {
                        currentTask = "Charging";
                    }
                    else
                    {
                        currentTask = "Available at Home";
                    }
                }
            }

            
            var pathfinder = GetComponent<RobotPathfinder>();
            bool isWaitingInQueue = pathfinder != null && 
                                    (pathfinder.state == RobotPathfinder.RobotState.WaitingForDestinationQueue || 
                                     pathfinder.state == RobotPathfinder.RobotState.WaitingForChargingQueue);

            if (currentTask == "Charging")
            {
                battery += Time.deltaTime * 5.0f; 
                if (battery > 100f)
                {
                    battery = 100f;
                    currentTask = "Available at Home";
                }
            }
            else if (isWaitingInQueue)
            {
                
            }
            else
            {
                
                float drain = (batteryConsumptionRate + (loadWeight * 0.0002f)) * (1.0f + (speed * 0.5f));
                if (isBatteryFault) drain *= 4.0f; 

                battery -= drain * Time.deltaTime;
                if (battery < 0f) battery = 0f;
            }

            
            float targetTemp = ambientTemperature + (speed * heatGenerationFactor) + (loadWeight * 0.02f);
            if (isOverheated) targetTemp += 45.0f; 

            if (temperature < targetTemp)
            {
                temperature += Time.deltaTime * 1.5f;
            }
            else
            {
                temperature -= Time.deltaTime * coolDownRate;
            }

            
            if (temperature < ambientTemperature) temperature = ambientTemperature;

            
            if (battery <= 0f)
            {
                status = "Critical (Depleted)";
                currentTask = "Offline";
            }
            else if (temperature > 75f || battery < 15f)
            {
                status = "Critical";
            }
            else if (temperature > 55f || battery < 30f)
            {
                status = "Warning";
            }
            else
            {
                status = "Nominal";
            }
        }

        
        public void InjectMotorOverheat(bool active)
        {
            isOverheated = active;
        }

        public void InjectBatteryFault(bool active)
        {
            isBatteryFault = active;
        }

        public void ResetFaults()
        {
            isOverheated = false;
            isBatteryFault = false;
        }


    }
}
