using System.Collections;
using System.Collections.Generic;
using System.Linq;
using UnityEditor;
using UnityEngine;

namespace WarehouseDigitalTwin
{
    [System.Serializable]
    public class RobotTaskAssignment
    {
        public RobotPathfinder robot;
        public Cardbox assignedCardbox;
    }

    public class AutomationWareHouseManager : MonoBehaviour
    {
        [Header("Global Settings")]
        [Tooltip("The first destination where robots will drop their assigned cardboxes.")]
        public Transform destination1;
        [Tooltip("The second destination where robots will drop their assigned cardboxes.")]
        public Transform destination2;

        [Header("Queue System")]
        [Tooltip("Where robots should wait if the destination is occupied.")]
        public Transform destinationWaitingPoint;
        public Queue<RobotPathfinder> dropoffQueue = new Queue<RobotPathfinder>();
        public RobotPathfinder currentDropoffRobot1;
        public RobotPathfinder currentDropoffRobot2;

        [Header("Charging System")]
        public float chargingDuration = 10f;
        public Queue<RobotPathfinder> chargingQueue = new Queue<RobotPathfinder>();
        public RobotPathfinder currentChargingRobot;
        private float currentChargeTimer = 0f;
        private readonly Dictionary<Cardbox, float> assignmentCooldownUntil = new Dictionary<Cardbox, float>();

        [Header("Assignment Performance")]
        [Min(0.1f)] public float assignmentCheckInterval = 0.35f;
        [Min(0.1f)] public float routeDistanceCacheDuration = 1.0f;
        private float nextAssignmentCheckTime;

        public GameObject dashboardController;
        private struct CachedRouteDistance
        {
            public float distance;
            public float expiresAt;
            public Vector3 robotPosition;
            public Vector3 cardboxPosition;
        }

        private readonly Dictionary<long, CachedRouteDistance> routeDistanceCache = new Dictionary<long, CachedRouteDistance>();

        [Header("Initial Assignment")]
        public bool randomizeInitialPackageAssignments = true;
        private bool initialAssignmentBatchCompleted;

        [Header("Robots")]
        [Tooltip("List of robots available for automation. Leave 'assignedCardbox' empty in Inspector.")]
        public List<RobotTaskAssignment> robotAssignments = new List<RobotTaskAssignment>();

        [Header("Cardboxes")]
        [Tooltip("List of all cardboxes in the warehouse. Auto-populates on Start if left empty.")]
        public List<Cardbox> allCardboxes = new List<Cardbox>();

        private IEnumerator  Start()
        {
            yield return new WaitForSeconds(3f);

            dropoffQueue.Clear();
            chargingQueue.Clear();
            currentDropoffRobot1 = null;
            currentDropoffRobot2 = null;
            currentChargingRobot = null;
            initialAssignmentBatchCompleted = false;
            nextAssignmentCheckTime = 0f;

            for (int i = 0; i < robotAssignments.Count; i++)
            {
                RobotTaskAssignment assignment = robotAssignments[i];
                assignment.assignedCardbox = null;
                if (assignment.robot == null) continue;

                assignment.robot.PrepareForAutomationStart();
                assignment.robot.SetAvoidancePriority(Mathf.Clamp(20 + (i * 6), 0, 95));
            }

            
            if (allCardboxes == null || allCardboxes.Count == 0)
            {
                allCardboxes = FindObjectsOfType<Cardbox>().ToList();
            }

            allCardboxes.RemoveAll(cb => cb == null);
            foreach (Cardbox cardbox in allCardboxes)
            {
                if (!cardbox.shipped)
                    cardbox.pickupAssigned = false;
            }
        }

        private void Update()
        {
            ManageDropoffQueue();
            ManageChargingQueue();

            if (Time.time >= nextAssignmentCheckTime)
            {
                nextAssignmentCheckTime = Time.time + assignmentCheckInterval;
                AssignTasks();
            }

            UpdateQueuePositions();

            

            if (Input.GetKeyDown(KeyCode.L))
            {
                robotAssignments[0].robot.GetComponent<RobotTwin>().battery = 9.5f;
            }

            if (Input.GetKeyDown(KeyCode.Tab))
            {
                dashboardController.SetActive(!dashboardController.activeSelf);
            }

        }

        private void UpdateQueuePositions()
        {
            
            foreach (var assignment in robotAssignments)
            {
                if (assignment.robot != null)
                {
                    assignment.robot.queuePosition = -1;
                }
            }

            
            int dropoffIdx = 1;
            foreach (var r in dropoffQueue)
            {
                if (r != null)
                {
                    r.queuePosition = dropoffIdx;
                }
                dropoffIdx++;
            }

            
            int chargingIdx = 1;
            foreach (var r in chargingQueue)
            {
                if (r != null)
                {
                    r.queuePosition = chargingIdx;
                }
                chargingIdx++;
            }
        }

        private void ManageChargingQueue()
        {
            
            if (currentChargingRobot != null)
            {
                if (currentChargingRobot.state == RobotPathfinder.RobotState.Charging)
                {
                    currentChargeTimer += Time.deltaTime;
                    if (currentChargeTimer >= chargingDuration)
                    {
                        currentChargingRobot.FinishCharging();
                        currentChargeTimer = 0f;
                    }
                }
                else if (currentChargingRobot.state == RobotPathfinder.RobotState.AvailableForAssignment ||
                         currentChargingRobot.state == RobotPathfinder.RobotState.NavigatingToPackage ||
                         currentChargingRobot.state == RobotPathfinder.RobotState.NavigatingHome)
                {
                    
                    currentChargingRobot = null;
                    currentChargeTimer = 0f;
                }
            }

            
            foreach (var assignment in robotAssignments)
            {
                var r = assignment.robot;
                if (r == null) continue;

                if (r.state == RobotPathfinder.RobotState.WaitingForChargingQueue)
                {
                    if (!chargingQueue.Contains(r) && currentChargingRobot != r)
                    {
                        chargingQueue.Enqueue(r);
                    }
                }
            }

            
            if (currentChargingRobot == null && chargingQueue.Count > 0)
            {
                currentChargingRobot = chargingQueue.Dequeue();
                currentChargingRobot.GrantChargingPermission(currentChargingRobot.robotTwin.chargingStation);
            }
        }

        private void ManageDropoffQueue()
        {
            
            if (currentDropoffRobot1 != null)
            {
                if (currentDropoffRobot1.state == RobotPathfinder.RobotState.LoweringEmptyToGround ||
                    currentDropoffRobot1.state == RobotPathfinder.RobotState.NavigatingHome ||
                    currentDropoffRobot1.state == RobotPathfinder.RobotState.AvailableAtHome ||
                    currentDropoffRobot1.state == RobotPathfinder.RobotState.Completed)
                {
                    currentDropoffRobot1 = null;
                }
            }

            if (currentDropoffRobot2 != null)
            {
                if (currentDropoffRobot2.state == RobotPathfinder.RobotState.LoweringEmptyToGround ||
                    currentDropoffRobot2.state == RobotPathfinder.RobotState.NavigatingHome ||
                    currentDropoffRobot2.state == RobotPathfinder.RobotState.AvailableAtHome ||
                    currentDropoffRobot2.state == RobotPathfinder.RobotState.Completed)
                {
                    currentDropoffRobot2 = null;
                }
            }

            
            foreach (var assignment in robotAssignments)
            {
                var r = assignment.robot;
                if (r == null) continue;

                if (r.state == RobotPathfinder.RobotState.WaitingForDestinationQueue)
                {
                    if (!dropoffQueue.Contains(r) && currentDropoffRobot1 != r && currentDropoffRobot2 != r)
                    {
                        dropoffQueue.Enqueue(r);
                    }
                }
            }

            
            if (dropoffQueue.Count > 0)
            {
                if (currentDropoffRobot1 == null)
                {
                    currentDropoffRobot1 = dropoffQueue.Dequeue();
                    currentDropoffRobot1.GrantDropoffPermission(destination1);
                }
                else if (currentDropoffRobot2 == null)
                {
                    currentDropoffRobot2 = dropoffQueue.Dequeue();
                    currentDropoffRobot2.GrantDropoffPermission(destination2);
                }
            }
        }

        [ContextMenu("List All Cardboxes")]
        public  void ListAllCardboxes()
        {
            GameObject[] allGameObjects = GameObject.FindObjectsOfType<GameObject>();
            List<GameObject> cardboxGameObjects = new List<GameObject>();

            foreach (GameObject go in allGameObjects)
            {
                Cardbox cardbox = go.GetComponent<Cardbox>();
                if (cardbox != null)
                {
                    allCardboxes.Add(cardbox);
                }
            }


  
        }

        private void RemoveRobotFromCharging(RobotPathfinder r)
        {
            if (currentChargingRobot == r)
            {
                currentChargingRobot = null;
                currentChargeTimer = 0f;
            }

            if (chargingQueue.Contains(r))
            {
                List<RobotPathfinder> temp = chargingQueue.ToList();
                temp.Remove(r);
                chargingQueue = new Queue<RobotPathfinder>(temp);
            }
        }

        private float GetCachedRouteDistance(RobotPathfinder robot, Cardbox cardbox)
        {
            if (robot == null || cardbox == null) return float.PositiveInfinity;
            long key = ((long)(uint)robot.GetInstanceID() << 32) | (uint)cardbox.GetInstanceID();
            Vector3 robotPosition = robot.transform.position;
            Vector3 cardboxPosition = cardbox.transform.position;

            if (routeDistanceCache.TryGetValue(key, out CachedRouteDistance cached) &&
                Time.time < cached.expiresAt &&
                Vector3.SqrMagnitude(cached.robotPosition - robotPosition) < 0.25f &&
                Vector3.SqrMagnitude(cached.cardboxPosition - cardboxPosition) < 0.01f)
            {
                return cached.distance;
            }

            float distance = robot.EstimateShortestPathDistance(cardboxPosition);
            routeDistanceCache[key] = new CachedRouteDistance
            {
                distance = distance,
                expiresAt = Time.time + routeDistanceCacheDuration,
                robotPosition = robotPosition,
                cardboxPosition = cardboxPosition
            };
            return distance;
        }
        private float CalculateTaskAssignmentScore(RobotPathfinder r, Cardbox box, out string reasoning, out float confidence)
        {
            float battery = r.robotTwin != null ? r.robotTwin.battery : 100f;
            float distance = GetCachedRouteDistance(r, box);
            if (float.IsInfinity(distance))
            {
                reasoning = "No complete NavMesh path";
                confidence = 0f;
                return float.MinValue;
            }
            float speed = r.robotTwin != null ? r.robotTwin.assignedSpeed : 3.5f;
            if (speed <= 0.1f) speed = 3.5f;
            float travelTime = distance / speed;
            int queuePos = r.queuePosition >= 1 ? r.queuePosition : 0;

            
            float score = (battery * 0.3f) - (distance * 0.3f) - (queuePos * 0.2f) - (travelTime * 0.2f);

            
            float rawConf = 100f - (distance * 1.5f) - (queuePos * 10f) + (battery * 0.2f);
            confidence = Mathf.Clamp(rawConf, 10f, 99f);

            reasoning = $"Battery: {battery:F0}%, Dist: {distance:F1}m, Queue Pos: {queuePos}, Est. Travel: {travelTime:F1}s";
            return score;
        }

        private void ShuffleInitialPackages(List<Cardbox> packages)
        {
            for (int i = packages.Count - 1; i > 0; i--)
            {
                int swapIndex = UnityEngine.Random.Range(0, i + 1);
                Cardbox temp = packages[i];
                packages[i] = packages[swapIndex];
                packages[swapIndex] = temp;
            }
        }
        private void AssignTasks()
        {
            
            
            allCardboxes.RemoveAll(cb => cb == null);

            
            foreach (var assignment in robotAssignments)
            {
                if (assignment.robot == null) continue;

                if (assignment.assignedCardbox != null && assignment.robot.IsAvailableForAssignment)
                {
                    if (assignment.assignedCardbox.shipped)
                    {
                        assignment.assignedCardbox = null;
                    }
                }
            }

            
            var availableBoxes = allCardboxes.Where(cb => cb != null && !cb.shipped && !cb.pickupAssigned && !IsBoxAssigned(cb) && (!assignmentCooldownUntil.TryGetValue(cb, out float until) || Time.time >= until)).ToList();
            bool isInitialAssignmentBatch = randomizeInitialPackageAssignments && !initialAssignmentBatchCompleted;
            if (isInitialAssignmentBatch)
            {
                ShuffleInitialPackages(availableBoxes);
            }
            if (availableBoxes.Count == 0)
            {
                foreach (var assignment in robotAssignments)
                {
                    if (assignment.robot == null || assignment.assignedCardbox != null) continue;
                    var robot = assignment.robot;
                    if (!robot.CanAcceptNewPackage) continue;
                    if (robot.robotTwin != null && robot.robotTwin.battery < 10f && robot.robotTwin.chargingStation != null)
                        robot.TriggerChargingSequence(robot.robotTwin.chargingStation);
                    else if (robot.state == RobotPathfinder.RobotState.AvailableForAssignment)
                        robot.ReturnHome();
                }
                return;
            }

            
            var readyAssignments = new List<RobotTaskAssignment>();
            foreach (var assignment in robotAssignments)
            {
                if (assignment.robot == null || assignment.assignedCardbox != null) continue;

                var r = assignment.robot;

                
                if (r.robotTwin != null && r.robotTwin.battery < 10.0f)
                {
                    if (r.IsAvailableForAssignment)
                    {
                        r.TriggerChargingSequence(r.robotTwin.chargingStation);
                    }
                    continue;
                }

                bool canTakeJob = r.CanAcceptNewPackage &&
                    (r.IsAvailableForAssignment || (r.robotTwin != null && r.robotTwin.battery > 30.0f));

                if (canTakeJob)
                {
                    readyAssignments.Add(assignment);
                }
            }

            if (readyAssignments.Count == 0) return;
            if (isInitialAssignmentBatch)
            {
                initialAssignmentBatchCompleted = true;
            }

            
            foreach (var chosenBox in availableBoxes)
            {
                if (readyAssignments.Count == 0) break;

                
                RobotTaskAssignment bestAssignment = null;
                float highestScore = float.MinValue;
                string bestReasoning = "";
                float bestConfidence = 0f;

                foreach (var assignment in readyAssignments)
                {
                    string reasoning;
                    float confidence;
                    float score = CalculateTaskAssignmentScore(assignment.robot, chosenBox, out reasoning, out confidence);
                    if (score > highestScore)
                    {
                        highestScore = score;
                        bestAssignment = assignment;
                        bestReasoning = reasoning;
                        bestConfidence = confidence;
                    }
                }

                if (bestAssignment == null)
                {
                    assignmentCooldownUntil[chosenBox] = Time.time + 10f;
                    continue;
                }

                if (bestAssignment != null)
                {
                    var r = bestAssignment.robot;

                    
                    Transform dest = destination1 != null ? destination1 : r.destinationTransform;

                    
                    Transform stackParent = chosenBox.transform.parent;
                    GameObject cargo;
                    if (stackParent != null && stackParent.name.StartsWith("CombinedStack_"))
                    {
                        cargo = stackParent.gameObject;
                    }
                    else
                    {
                        cargo = chosenBox.gameObject;
                    }

                    Cardbox[] cargoBoxes = cargo.GetComponentsInChildren<Cardbox>();
                    foreach (var cb in cargoBoxes)
                    {
                        cb.pickupAssigned = true;
                    }

                    
                    if (!r.TriggerJob(chosenBox, dest))
                    {
                        foreach (var cb in cargoBoxes)
                            if (!cb.shipped) cb.pickupAssigned = false;
                        continue;
                    }

                    RemoveRobotFromCharging(r);
                    bestAssignment.assignedCardbox = chosenBox;
                    readyAssignments.Remove(bestAssignment);

                    
                    string alertMsg = $"AI Assignment: Robot {r.robotTwin?.robotID ?? "Unknown"} assigned to box {chosenBox.cardboxID} (Score: {highestScore:F2}, Conf: {bestConfidence:F0}%). {bestReasoning}";
                   
                }
            }
        }

        public void ReportJobFailure(RobotPathfinder robot, string reason)
        {
            if (robot == null) return;
            RemoveRobotFromCharging(robot);
            if (dropoffQueue.Contains(robot))
                dropoffQueue = new Queue<RobotPathfinder>(dropoffQueue.Where(r => r != robot));
            if (currentDropoffRobot1 == robot) currentDropoffRobot1 = null;
            if (currentDropoffRobot2 == robot) currentDropoffRobot2 = null;

            RobotTaskAssignment assignment = robotAssignments.FirstOrDefault(a => a.robot == robot);
            Cardbox failedBox = assignment != null ? assignment.assignedCardbox : robot.targetCardbox;
            if (failedBox != null)
            {
                Transform root = failedBox.transform.parent != null && failedBox.transform.parent.name.StartsWith("CombinedStack_")
                    ? failedBox.transform.parent : failedBox.transform;
                foreach (var cb in root.GetComponentsInChildren<Cardbox>())
                    if (!cb.shipped) cb.pickupAssigned = false;
                assignmentCooldownUntil[failedBox] = Time.time + 10f;
            }
            if (assignment != null) assignment.assignedCardbox = null;
            robot.CancelCurrentTaskAndReturn();
            Debug.LogWarning($"[AutomationWareHouseManager] Released failed assignment for {robot.name}: {reason}");
        }
        private bool IsBoxAssigned(Cardbox box)
        {
            foreach (var assignment in robotAssignments)
            {
                if (assignment.assignedCardbox == box) return true;
            }
            return false;
        }
    }
}
