using System;
using System.Collections.Generic;
using UnityEngine;
using UnityEngine.AI;

namespace WarehouseDigitalTwin
{
    [RequireComponent(typeof(NavMeshAgent))]
    [RequireComponent(typeof(Rigidbody))]
    [RequireComponent(typeof(BoxCollider))]
    public class RobotPathfinder : MonoBehaviour
    {
        public enum RobotState
        {
            AvailableAtHome,
            NavigatingToPackage,
            AligningForPickup,
            LiftingToSourceHeight,
            DockingIntoSourceShelf,
            GrabbingCargo,
            UndockingFromSourceShelf,
            LoweringToGroundWithCargo,
            WaitingForDestinationQueue,
            NavigatingToDropoff,
            AligningForDropoff,
            LiftingToDestHeight,
            DockingIntoDestShelf,
            PlacingCargo,
            UndockingFromDestShelf,
            LoweringEmptyToGround,
            NavigatingHome,
            WaitingForChargingQueue,
            NavigatingToChargingStation,
            Charging,
            Completed,
            AvailableForAssignment,
            LeavingChargingStation
        }

        [Header("Targeting & Destination")]
        [Tooltip("The unique ID of the target cardboard box to retrieve.")]
        [HideInInspector]
        public string targetCardboxID;
        
        private bool pathRequested = false;
        private float pathRetryTimer = 0f;
        [HideInInspector]
        public Vector3 currentWaitPos = Vector3.zero;
        [Tooltip("The target cardboard box component reference.")]
        public Cardbox targetCardbox;
        [Tooltip("The destination transform where the cardboard box/stack will be delivered.")]
        public Transform destinationTransform;

        [Header("AMR Reach Truck Parameters")]
        [Tooltip("Distance in front of the shelf to park safely before lifting (meters).")]
        public float frontOffsetDist = 1.0f;
        [Tooltip("Vertical speed of the robot when lifting itself.")]
        public float liftSpeed = 1.5f;
        [Tooltip("Horizontal speed of the robot when docking/undocking inside the shelf.")]
        public float horizontalDockSpeed = 1.2f;
        [Tooltip("Time spent placing or retrieving cargo (seconds).")]
        public float cargoHandlingDuration = 1.5f;
        [Tooltip("Local Y offset for the cargo to sit neatly on top of the robot deck when carrying it.")]
        public float cargoCarryingLocalY = 0.22f;
        [Tooltip("Clearance height when sliding the deck under the pallet (meters).")]
        public float clearance = 0.05f;
        [Tooltip("Extra vertical lift height to clear shelf beams when retrieving/placing (meters).")]
        public float liftOffHeight = 0.05f;
        [Tooltip("Vertical height of the pallet fork pocket/gap center from the bottom of the pallet.")]
        public float palletGapOffset = 0.06f;
        [Tooltip("Vertical height where deck touches the underside of the top board of the pallet.")]
        public float palletGrabTouchOffset = 0.10f;
        [Tooltip("Vertical offset for placing cargo at the destination (e.g. raised conveyor surface).")]
        public float destinationYOffset = 0f;
        [Tooltip("Speed at which the conveyor belt moves the pallet (m/s).")]
        public float conveyorBeltSpeed = 1.0f;
        [Tooltip("If true, the job starts automatically when Play mode begins.")]
        public bool startOnPlay = true;

        [Header("Queue Status")]
        [Tooltip("Position in the waiting queue (1 = first, 2 = second, etc., -1 if not in queue)")]
        public int queuePosition = -1;
        [Tooltip("The gap maintained between robots in waiting queues (meters).")]
        public float queueGap = 2.5f;

        private AutomationWareHouseManager manager;

        [Header("Status & State")]
        public RobotState state = RobotState.AvailableAtHome;

        
        private NavMeshAgent navAgent;
        private UnityEngine.AI.NavMeshObstacle navObstacle;
        private Rigidbody rb;
        public RobotTwin robotTwin;

        private Vector3 homePosition;
        private Quaternion homeRotation;
        private GameObject cargo;
        private Transform originalCargoParent;
        private Vector3 originalCargoPosition;
        private Quaternion originalCargoRotation;
        
        private Transform activeShelf;
        private Vector3 dockingNormal;
        private float stateTimer = 0f;
        private float stuckTimer = 0f;
        private const int MaxPathAttempts = 3;
        private int pathFailureCount = 0;
        private Vector3 lastRouteTarget;
        private Vector3 chargingExitPosition;
        private Vector3 chargingApproachDirection;
        private bool hasRouteTarget = false;

        
        private Vector3 sourceSafeParkingPos;
        private Vector3 sourceNavigationTarget;
        private Vector3 destSafeParkingPos;
        private float cargoOriginalHeight;
        private Vector3 cargoVisualCenter;  
        private int grabStage = 0;
        private int placeStage = 0;

        private void Start()
        {
            
            navAgent = GetComponent<NavMeshAgent>();
            rb = GetComponent<Rigidbody>();
            robotTwin = GetComponent<RobotTwin>();
            manager = FindObjectOfType<AutomationWareHouseManager>();

            navObstacle = GetComponent<UnityEngine.AI.NavMeshObstacle>();
            if (navObstacle == null)
            {
                navObstacle = gameObject.AddComponent<UnityEngine.AI.NavMeshObstacle>();
                navObstacle.shape = UnityEngine.AI.NavMeshObstacleShape.Box;
                navObstacle.size = new Vector3(1.5f, 2.0f, 1.5f);
                navObstacle.carving = true;
                navObstacle.enabled = false;
            }
            else
            {
                navObstacle.carving = true;
            }

            if (navAgent != null)
            {
                if (navAgent.radius < 0.6f) navAgent.radius = 0.6f;
                navAgent.obstacleAvoidanceType = UnityEngine.AI.ObstacleAvoidanceType.HighQualityObstacleAvoidance;
                navAgent.avoidancePriority = 50;
            }

            
            if (rb != null)
            {
                rb.isKinematic = true;
            }

            
            homePosition = transform.position;
            homeRotation = transform.rotation;

            
            
            if (state == RobotState.Completed || state == RobotState.AvailableAtHome)
            {
                PrepareForAutomationStart();
            }
        }

        public void PrepareForAutomationStart()
        {
            targetCardbox = null;
            targetCardboxID = string.Empty;
            destinationTransform = null;
            cargo = null;
            activeShelf = null;
            originalCargoParent = null;
            queuePosition = -1;
            pathRequested = false;
            pathRetryTimer = 0f;
            pathFailureCount = 0;
            hasRouteTarget = false;
            stuckTimer = 0f;
            state = RobotState.AvailableAtHome;
        }

        private void SetAgentActive(bool isActive)
        {
            if (isActive)
            {
                if (navObstacle != null) navObstacle.enabled = false;
                if (navAgent != null)
                {
                    navAgent.enabled = true;
                    
                    if (!navAgent.isOnNavMesh)
                    {
                        NavMeshHit hit;
                        if (NavMesh.SamplePosition(transform.position, out hit, 2.0f, NavMesh.AllAreas))
                        {
                            navAgent.Warp(hit.position);
                        }
                    }
                }
            }
            else
            {
                if (navAgent != null) navAgent.enabled = false;
                if (navObstacle != null) navObstacle.enabled = true;
            }
        }

        private void Update()
        {
            
            if (robotTwin != null)
            {
                if (navAgent != null) navAgent.speed = robotTwin.assignedSpeed;
                robotTwin.currentTargetCardboxID = !string.IsNullOrEmpty(targetCardboxID) ? targetCardboxID : "None";
                robotTwin.currentTask = GetTaskNameForState(state);
            }

            
            if (targetCardbox == null && !string.IsNullOrEmpty(targetCardboxID))
            {
                ResolveTargetByID();
            }

            
            if (robotTwin != null && robotTwin.battery < 10.0f)
            {
                if (state == RobotState.AvailableAtHome || state == RobotState.AvailableForAssignment || state == RobotState.Completed)
                {
                    TriggerChargingSequence(robotTwin.chargingStation);
                }
            }

            UpdateStuckDetection();

            
            switch (state)
            {
                case RobotState.AvailableAtHome:
                case RobotState.AvailableForAssignment:
                    break;

                case RobotState.NavigatingToPackage:
                    UpdateNavigatingToPackage();
                    break;

                case RobotState.AligningForPickup:
                    UpdateAligningForPickup();
                    break;

                case RobotState.LiftingToSourceHeight:
                    UpdateLiftingToSourceHeight();
                    break;

                case RobotState.DockingIntoSourceShelf:
                    UpdateDockingIntoSourceShelf();
                    break;

                case RobotState.GrabbingCargo:
                    UpdateGrabbingCargo();
                    break;

                case RobotState.UndockingFromSourceShelf:
                    UpdateUndockingFromSourceShelf();
                    break;

                case RobotState.LoweringToGroundWithCargo:
                    UpdateLoweringToGroundWithCargo();
                    break;

                case RobotState.WaitingForDestinationQueue:
                    UpdateWaitingForDestinationQueue();
                    break;

                case RobotState.NavigatingToDropoff:
                    UpdateNavigatingToDropoff();
                    break;

                case RobotState.AligningForDropoff:
                    UpdateAligningForDropoff();
                    break;

                case RobotState.LiftingToDestHeight:
                    UpdateLiftingToDestHeight();
                    break;

                case RobotState.DockingIntoDestShelf:
                    UpdateDockingIntoDestShelf();
                    break;

                case RobotState.PlacingCargo:
                    UpdatePlacingCargo();
                    break;

                case RobotState.UndockingFromDestShelf:
                    UpdateUndockingFromDestShelf();
                    break;

                case RobotState.LoweringEmptyToGround:
                    UpdateLoweringEmptyToGround();
                    break;

                case RobotState.NavigatingHome:
                    UpdateNavigatingHome();
                    break;

                case RobotState.WaitingForChargingQueue:
                    UpdateWaitingForChargingQueue();
                    break;

                case RobotState.NavigatingToChargingStation:
                    UpdateNavigatingToChargingStation();
                    break;

                case RobotState.Charging:
                    
                    break;

                case RobotState.LeavingChargingStation:
                    UpdateLeavingChargingStation();
                    break;

                case RobotState.Completed:
                    SetState(RobotState.AvailableAtHome);
                    break;
            }
        }

        public bool TriggerJob(Cardbox box, Transform destination)
        {
            if (box == null || !CanAcceptNewPackage) return false;
            targetCardbox = box;
            targetCardboxID = box.cardboxID;
            destinationTransform = destination;
            InitializeJob();
            return true;
        }

        private void ResolveTargetByID()
        {
            var allCardboxes = FindObjectsOfType<Cardbox>();
            foreach (var cb in allCardboxes)
            {
                if (cb.cardboxID == targetCardboxID)
                {
                    targetCardbox = cb;
                    break;
                }
            }
        }

        private void InitializeJob()
        {
            if (targetCardbox == null) return;

            
            Transform stackParent = targetCardbox.transform.parent;
            if (stackParent != null && stackParent.name.StartsWith("CombinedStack_"))
            {
                cargo = stackParent.gameObject;
            }
            else
            {
                cargo = targetCardbox.gameObject;
            }

            originalCargoParent = cargo.transform.parent;
            originalCargoPosition = cargo.transform.position;
            originalCargoRotation = cargo.transform.rotation;
            cargoOriginalHeight = cargo.transform.position.y;

            
            cargoVisualCenter = GetObjectCenter(cargo);

            
            activeShelf = targetCardbox.assignedShelf;
            if (activeShelf == null)
            {
                activeShelf = FindClosestShelf(cargoVisualCenter);
            }
            
            dockingNormal = GetDockingNormal(null, cargoVisualCenter, activeShelf);

            
            
            
            sourceSafeParkingPos = BuildSafeParkingPosFromShelf(cargoVisualCenter, dockingNormal, activeShelf, frontOffsetDist);
            sourceSafeParkingPos.y = transform.position.y;
            sourceNavigationTarget = sourceSafeParkingPos;
            if (NavMesh.SamplePosition(sourceSafeParkingPos, out NavMeshHit sourceHit, 3f, NavMesh.AllAreas))
                sourceNavigationTarget = sourceHit.position;

            SetAgentActive(true);
            navAgent.stoppingDistance = 0.05f;

            SetState(RobotState.NavigatingToPackage);
        }

        private void UpdateNavigatingToPackage()
        {
            if (!navAgent.isOnNavMesh)
            {
                NavMeshHit hit;
                if (NavMesh.SamplePosition(transform.position, out hit, 10.0f, NavMesh.AllAreas))
                {
                    navAgent.Warp(hit.position);
                }
                else
                {
                    return;
                }
            }

            
            
            float arrivalDistance = Vector2.Distance(
                new Vector2(transform.position.x, transform.position.z),
                new Vector2(sourceNavigationTarget.x, sourceNavigationTarget.z));

            if (arrivalDistance <= Mathf.Max(navAgent.stoppingDistance + 0.15f, 0.35f))
            {
                SetAgentActive(false);
                SetState(RobotState.AligningForPickup);
                return;
            }

            if (!EnsureShortestPath(sourceNavigationTarget)) return;

            if (navAgent.pathPending) return;

            if (pathRequested && !navAgent.hasPath)
            {
                pathRetryTimer += Time.deltaTime;
                if (pathRetryTimer > 1.0f)
                {
                    pathRequested = false;
                    pathRetryTimer = 0f;
                }
                return;
            }
            pathRetryTimer = 0f;
        }

        private void UpdateAligningForPickup()
        {
            
            Vector3 targetGroundPos = sourceSafeParkingPos;
            targetGroundPos.y = transform.position.y;

            
            transform.position = Vector3.MoveTowards(transform.position, targetGroundPos, Time.deltaTime * 1.5f);

            
            Quaternion targetRotation = Quaternion.LookRotation(-dockingNormal);
            transform.rotation = Quaternion.Slerp(transform.rotation, targetRotation, Time.deltaTime * 8f);

            float posDist = Vector2.Distance(new Vector2(transform.position.x, transform.position.z), new Vector2(targetGroundPos.x, targetGroundPos.z));
            float rotAngle = Quaternion.Angle(transform.rotation, targetRotation);

            if (posDist < 0.02f && rotAngle < 1f)
            {
                transform.position = targetGroundPos;
                transform.rotation = targetRotation;
                grabStage = 0;
                SetState(RobotState.LiftingToSourceHeight);
            }
        }

        private void UpdateLiftingToSourceHeight()
        {
            
            float targetHeight = Mathf.Max(homePosition.y, cargoOriginalHeight + palletGapOffset - cargoCarryingLocalY);
            Vector3 targetPos = new Vector3(transform.position.x, targetHeight, transform.position.z);

            transform.position = Vector3.MoveTowards(transform.position, targetPos, liftSpeed * Time.deltaTime);

            if (Mathf.Abs(transform.position.y - targetHeight) < 0.02f)
            {
                transform.position = targetPos;
                SetState(RobotState.DockingIntoSourceShelf);
            }
        }

        private void UpdateDockingIntoSourceShelf()
        {
            
            
            
            Vector3 dockTarget = MovedAlongDockingAxis(transform.position, cargoVisualCenter, dockingNormal);
            dockTarget.y = transform.position.y; 

            transform.position = Vector3.MoveTowards(transform.position, dockTarget, horizontalDockSpeed * Time.deltaTime);

            float dist = Vector3.Distance(
                new Vector3(transform.position.x, 0, transform.position.z),
                new Vector3(dockTarget.x, 0, dockTarget.z));

            if (dist < 0.02f)
            {
                transform.position = dockTarget;
                grabStage = 0;
                SetState(RobotState.GrabbingCargo);
            }
        }

        private void UpdateGrabbingCargo()
        {
            if (grabStage == 0)
            {
                
                float touchHeight = Mathf.Max(homePosition.y, cargoOriginalHeight + palletGrabTouchOffset - cargoCarryingLocalY);
                Vector3 targetPos = new Vector3(transform.position.x, touchHeight, transform.position.z);
                transform.position = Vector3.MoveTowards(transform.position, targetPos, liftSpeed * Time.deltaTime);

                if (Mathf.Abs(transform.position.y - touchHeight) < 0.01f)
                {
                    transform.position = targetPos;
                    grabStage = 1;
                }
            }
            else if (grabStage == 1)
            {
                
                if (cargo != null)
                {
                    
                    Vector3 cargoCenter = GetObjectCenter(cargo);
                    Vector3 pivotToCenterOffset = cargo.transform.position - cargoCenter;
                    pivotToCenterOffset.y = 0; 

                    cargo.transform.SetParent(transform, true);
                    
                    
                    
                    Vector3 localOffset = transform.InverseTransformDirection(pivotToCenterOffset);
                    cargo.transform.localPosition = new Vector3(localOffset.x, cargoCarryingLocalY, localOffset.z);
                    cargo.transform.localRotation = Quaternion.identity;

                    foreach (var cb in cargo.GetComponentsInChildren<Cardbox>())
                    {
                        cb.isPalletized = true;
                    }
                    Debug.Log($"[RobotPathfinder] Attached cargo '{cargo.name}' to robot transform (visual center aligned).");
                }
                grabStage = 2;
            }
            else if (grabStage == 2)
            {
                
                float touchHeight = Mathf.Max(homePosition.y, cargoOriginalHeight + palletGrabTouchOffset - cargoCarryingLocalY);
                float targetHeight = touchHeight + liftOffHeight;
                Vector3 targetPos = new Vector3(transform.position.x, targetHeight, transform.position.z);
                transform.position = Vector3.MoveTowards(transform.position, targetPos, liftSpeed * Time.deltaTime);

                if (Mathf.Abs(transform.position.y - targetHeight) < 0.01f)
                {
                    transform.position = targetPos;
                    stateTimer = cargoHandlingDuration;
                    grabStage = 3;
                }
            }
            else if (grabStage == 3)
            {
                
                stateTimer -= Time.deltaTime;
                if (stateTimer <= 0)
                {
                    SetState(RobotState.UndockingFromSourceShelf);
                }
            }
        }

        private void UpdateUndockingFromSourceShelf()
        {
            
            Vector3 undockedPos = sourceSafeParkingPos;
            undockedPos.y = transform.position.y; 

            transform.position = Vector3.MoveTowards(transform.position, undockedPos, horizontalDockSpeed * Time.deltaTime);

            if (Vector2.Distance(new Vector2(transform.position.x, transform.position.z), new Vector2(undockedPos.x, undockedPos.z)) < 0.02f)
            {
                transform.position = undockedPos;
                SetState(RobotState.LoweringToGroundWithCargo);
            }
        }

        private void UpdateLoweringToGroundWithCargo()
        {
            
            float targetHeight = homePosition.y;
            Vector3 targetPos = new Vector3(transform.position.x, targetHeight, transform.position.z);

            transform.position = Vector3.MoveTowards(transform.position, targetPos, liftSpeed * Time.deltaTime);

            if (Mathf.Abs(transform.position.y - targetHeight) < 0.02f)
            {
                transform.position = targetPos;

                SetAgentActive(true);
                SetState(RobotState.WaitingForDestinationQueue);
            }
        }



        public void GrantDropoffPermission(Transform selectedDestination)
        {
            if (selectedDestination != null)
            {
                destinationTransform = selectedDestination;
                dockingNormal = -destinationTransform.right;
                
                destSafeParkingPos = destinationTransform.position + dockingNormal * frontOffsetDist;
                destSafeParkingPos.y = homePosition.y;

                SetAgentActive(true);
                navAgent.stoppingDistance = 0.05f;

                SetState(RobotState.NavigatingToDropoff);
            }
        }

        private void UpdateNavigatingToDropoff()
        {
            if (!navAgent.isOnNavMesh)
            {
                NavMeshHit hit;
                if (NavMesh.SamplePosition(transform.position, out hit, 10.0f, NavMesh.AllAreas))
                {
                    navAgent.Warp(hit.position);
                }
                else
                {
                    return;
                }
            }

            if (!EnsureShortestPath(destSafeParkingPos)) return;

            if (navAgent.pathPending) return;

            if (pathRequested && !navAgent.hasPath)
            {
                pathRetryTimer += Time.deltaTime;
                if (pathRetryTimer > 1.0f)
                {
                    pathRequested = false;
                    pathRetryTimer = 0f;
                }
                return;
            }
            pathRetryTimer = 0f;

            float trueDist = Vector2.Distance(new Vector2(transform.position.x, transform.position.z), new Vector2(destSafeParkingPos.x, destSafeParkingPos.z));

            if (trueDist <= Mathf.Max(navAgent.stoppingDistance + 0.1f, 0.25f))
            {
                SetAgentActive(false);
                SetState(RobotState.AligningForDropoff);
            }
        }

        private void UpdateAligningForDropoff()
        {
            
            Vector3 targetGroundPos = destSafeParkingPos;
            targetGroundPos.y = transform.position.y;
            transform.position = Vector3.MoveTowards(transform.position, targetGroundPos, Time.deltaTime * 1.5f);

            
            Quaternion targetRotation = Quaternion.LookRotation(-dockingNormal);
            transform.rotation = Quaternion.Slerp(transform.rotation, targetRotation, Time.deltaTime * 8f);

            float posDist = Vector2.Distance(new Vector2(transform.position.x, transform.position.z), new Vector2(targetGroundPos.x, targetGroundPos.z));
            float rotAngle = Quaternion.Angle(transform.rotation, targetRotation);

            if (posDist < 0.02f && rotAngle < 1f)
            {
                transform.position = targetGroundPos;
                transform.rotation = targetRotation;
                placeStage = 0;
                SetState(RobotState.LiftingToDestHeight);
            }
        }

        private void UpdateLiftingToDestHeight()
        {
            
            float targetHeight = Mathf.Max(homePosition.y, (destinationTransform.position.y + destinationYOffset) - cargoCarryingLocalY + liftOffHeight);
            Vector3 targetPos = new Vector3(transform.position.x, targetHeight, transform.position.z);

            transform.position = Vector3.MoveTowards(transform.position, targetPos, liftSpeed * Time.deltaTime);

            if (Mathf.Abs(transform.position.y - targetHeight) < 0.02f)
            {
                transform.position = targetPos;
                SetState(RobotState.DockingIntoDestShelf);
            }
        }

        private void UpdateDockingIntoDestShelf()
        {
            
            Vector3 dockedPos = destinationTransform.position;
            dockedPos.y = transform.position.y;

            transform.position = Vector3.MoveTowards(transform.position, dockedPos, horizontalDockSpeed * Time.deltaTime);

            if (Vector2.Distance(new Vector2(transform.position.x, transform.position.z), new Vector2(dockedPos.x, dockedPos.z)) < 0.02f)
            {
                transform.position = dockedPos;
                placeStage = 0;
                SetState(RobotState.PlacingCargo);
            }
        }

        private void UpdatePlacingCargo()
        {
            if (placeStage == 0)
            {
                
                float restHeight = Mathf.Max(homePosition.y, (destinationTransform.position.y + destinationYOffset) - cargoCarryingLocalY);
                Vector3 targetPos = new Vector3(transform.position.x, restHeight, transform.position.z);
                transform.position = Vector3.MoveTowards(transform.position, targetPos, liftSpeed * Time.deltaTime);

                if (Mathf.Abs(transform.position.y - restHeight) < 0.01f)
                {
                    transform.position = targetPos;
                    placeStage = 1;
                }
            }
            else if (placeStage == 1)
            {
                
                if (cargo != null)
                {
                    cargo.transform.SetParent(destinationTransform, true);
                    cargo.transform.position = destinationTransform.position + Vector3.up * destinationYOffset;
                    cargo.transform.rotation = destinationTransform.rotation;
                    foreach (var cb in cargo.GetComponentsInChildren<Cardbox>())
                    {
                        cb.isPalletized = true;
                        cb.shipped = true;
                        cb.StartConveyorMovement(conveyorBeltSpeed);
                    }
                    Debug.Log($"[RobotPathfinder] Placed cargo '{cargo.name}' at destination '{destinationTransform.name}'. Conveyor movement triggered.");
                    cargo = null;
                }
                placeStage = 2;
            }
            else if (placeStage == 2)
            {
                
                float restHeight = Mathf.Max(homePosition.y, (destinationTransform.position.y + destinationYOffset) - cargoCarryingLocalY);
                float targetHeight = Mathf.Max(homePosition.y, restHeight - palletGapOffset);
                Vector3 targetPos = new Vector3(transform.position.x, targetHeight, transform.position.z);
                transform.position = Vector3.MoveTowards(transform.position, targetPos, liftSpeed * Time.deltaTime);

                if (Mathf.Abs(transform.position.y - targetHeight) < 0.01f)
                {
                    transform.position = targetPos;
                    stateTimer = cargoHandlingDuration;
                    placeStage = 3;
                }
            }
            else if (placeStage == 3)
            {
                
                stateTimer -= Time.deltaTime;
                if (stateTimer <= 0)
                {
                    SetState(RobotState.UndockingFromDestShelf);
                }
            }
        }

        private void UpdateUndockingFromDestShelf()
        {
            
            Vector3 undockedPos = destSafeParkingPos;
            undockedPos.y = transform.position.y;

            transform.position = Vector3.MoveTowards(transform.position, undockedPos, horizontalDockSpeed * Time.deltaTime);

            if (Vector2.Distance(new Vector2(transform.position.x, transform.position.z), new Vector2(undockedPos.x, undockedPos.z)) < 0.02f)
            {
                transform.position = undockedPos;
                SetState(RobotState.LoweringEmptyToGround);
            }
        }

        private void UpdateLoweringEmptyToGround()
        {
            
            float targetHeight = homePosition.y;
            Vector3 targetPos = new Vector3(transform.position.x, targetHeight, transform.position.z);

            transform.position = Vector3.MoveTowards(transform.position, targetPos, liftSpeed * Time.deltaTime);

            if (Mathf.Abs(transform.position.y - targetHeight) < 0.02f)
            {
                transform.position = targetPos;
                
                MarkAvailableForAssignment();
                Debug.Log($"[RobotPathfinder] Job completed. Robot ready for next assignment.");
            }
        }

        private void UpdateNavigatingHome()
        {
            if (!navAgent.isOnNavMesh)
            {
                NavMeshHit hit;
                if (NavMesh.SamplePosition(transform.position, out hit, 10.0f, NavMesh.AllAreas))
                {
                    navAgent.Warp(hit.position);
                }
                else
                {
                    return;
                }
            }

            if (!EnsureShortestPath(homePosition)) return;

            if (navAgent.pathPending) return;

            if (pathRequested && !navAgent.hasPath)
            {
                pathRetryTimer += Time.deltaTime;
                if (pathRetryTimer > 1.0f)
                {
                    pathRequested = false;
                    pathRetryTimer = 0f;
                }
                return;
            }
            pathRetryTimer = 0f;

            float trueDist = Vector2.Distance(new Vector2(transform.position.x, transform.position.z), new Vector2(homePosition.x, homePosition.z));

            if (trueDist <= Mathf.Max(navAgent.stoppingDistance + 0.1f, 0.25f))
            {
                SetAgentActive(false);
                transform.rotation = Quaternion.Slerp(transform.rotation, homeRotation, Time.deltaTime * 6f);

                if (Quaternion.Angle(transform.rotation, homeRotation) < 5f)
                {
                    transform.rotation = homeRotation;
                    SetState(RobotState.AvailableAtHome);
                }
            }
        }

        public void TriggerChargingSequence(Transform waitPoint)
        {
            if (waitPoint != null && navAgent != null)
            {
                SetAgentActive(true);
                SetState(RobotState.WaitingForChargingQueue);
                Debug.Log($"[RobotPathfinder] Low Battery! Waiting in queue for charging.");
            }
        }

        public void GrantChargingPermission(Transform chargePad)
        {
            if (chargePad != null && navAgent != null)
            {
                Vector3 approach = chargePad.position - transform.position;
                approach.y = 0f;
                if (approach.sqrMagnitude > 0.001f)
                    chargingApproachDirection = approach.normalized;

                SetAgentActive(true);
                navAgent.stoppingDistance = 0.05f;
                navAgent.SetDestination(chargePad.position);
                SetState(RobotState.NavigatingToChargingStation);
                Debug.Log($"[RobotPathfinder] Charging permission granted. Navigating to charging station.");
            }
        }

        private void UpdateNavigatingToChargingStation()
        {
            if (!navAgent.isOnNavMesh)
            {
                NavMeshHit hit;
                if (NavMesh.SamplePosition(transform.position, out hit, 10.0f, NavMesh.AllAreas))
                {
                    navAgent.Warp(hit.position);
                }
                else
                {
                    return;
                }
            }

            Vector3 chargeDest = (robotTwin != null && robotTwin.chargingStation != null) ? robotTwin.chargingStation.position : navAgent.destination;

            if (!EnsureShortestPath(chargeDest)) return;

            if (navAgent.pathPending) return;

            if (pathRequested && !navAgent.hasPath)
            {
                pathRetryTimer += Time.deltaTime;
                if (pathRetryTimer > 1.0f)
                {
                    pathRequested = false;
                    pathRetryTimer = 0f;
                }
                return;
            }
            pathRetryTimer = 0f;

            float trueDist = Vector2.Distance(new Vector2(transform.position.x, transform.position.z), new Vector2(chargeDest.x, chargeDest.z));

            if (trueDist <= Mathf.Max(navAgent.stoppingDistance + 0.1f, 0.25f))
            {
                
                
                Vector3 finalHeading = transform.forward;
                finalHeading.y = 0f;
                if (finalHeading.sqrMagnitude > 0.001f)
                    chargingApproachDirection = finalHeading.normalized;

                SetAgentActive(false); 
                SetState(RobotState.Charging);
            }
        }

        public void FinishCharging()
        {
            if (robotTwin != null)
            {
                robotTwin.battery = 100f;
            }

            if (chargingApproachDirection.sqrMagnitude < 0.001f)
            {
                chargingApproachDirection = transform.forward;
                chargingApproachDirection.y = 0f;
                chargingApproachDirection.Normalize();
            }

            chargingExitPosition = transform.position -
                chargingApproachDirection * Mathf.Max(frontOffsetDist, 1.5f);
            chargingExitPosition.y = homePosition.y;

            SetAgentActive(false);
            SetState(RobotState.LeavingChargingStation);
            Debug.Log($"[RobotPathfinder] Charging complete. Reversing clear of the station.");
        }

        private void UpdateLeavingChargingStation()
        {
            
            transform.position = Vector3.MoveTowards(
                transform.position,
                chargingExitPosition,
                horizontalDockSpeed * Time.deltaTime);

            float distance = Vector2.Distance(
                new Vector2(transform.position.x, transform.position.z),
                new Vector2(chargingExitPosition.x, chargingExitPosition.z));

            if (distance <= 0.02f)
            {
                transform.position = chargingExitPosition;
                SetAgentActive(true);
                MarkAvailableForAssignment();
                Debug.Log($"[RobotPathfinder] Cleared charging station. Ready for package assignment.");
            }
        }

        private void UpdateWaitingForDestinationQueue()
        {
            if (manager == null || manager.destinationWaitingPoint == null) return;

            
            int pos = queuePosition >= 1 ? queuePosition : 1;

            
            Vector3 targetPos = manager.destinationWaitingPoint.position - manager.destinationWaitingPoint.forward * queueGap * (pos - 1);
            Quaternion targetRot = manager.destinationWaitingPoint.rotation;

            UpdateWaitingQueue(targetPos, targetRot);
        }

        private void UpdateWaitingForChargingQueue()
        {
            if (robotTwin == null || robotTwin.chargingStation == null) return;

            
            int pos = queuePosition >= 1 ? queuePosition : 1;

            
            Vector3 targetPos = robotTwin.chargingStation.position - robotTwin.chargingStation.forward * queueGap * pos;
            Quaternion targetRot = robotTwin.chargingStation.rotation;

            UpdateWaitingQueue(targetPos, targetRot);
        }

        private void UpdateWaitingQueue(Vector3 targetPos, Quaternion targetRot)
        {
            if (navAgent == null) return;

            float distToTarget = Vector3.Distance(new Vector3(transform.position.x, 0, transform.position.z), new Vector3(targetPos.x, 0, targetPos.z));

            if (distToTarget > 0.15f)
            {
                if (!navAgent.enabled)
                {
                    SetAgentActive(true);
                }
                if (navAgent.isOnNavMesh)
                {
                    navAgent.stoppingDistance = 0.05f;
                    navAgent.SetDestination(targetPos);
                }
            }
            else
            {
                
                if (navAgent.enabled)
                {
                    SetAgentActive(false);
                }

                
                Vector3 targetGroundPos = targetPos;
                targetGroundPos.y = transform.position.y;
                transform.position = Vector3.MoveTowards(transform.position, targetGroundPos, Time.deltaTime * 1.5f);
                transform.rotation = Quaternion.Slerp(transform.rotation, targetRot, Time.deltaTime * 8f);
            }
        }

        private Transform FindClosestShelf(Vector3 searchPosition)
        {
            Collider[] hitColliders = Physics.OverlapSphere(searchPosition, 8.0f);
            Transform closestShelf = null;
            float minDist = float.MaxValue;
            foreach (var col in hitColliders)
            {
                if (col.gameObject.CompareTag("Shelf"))
                {
                    
                    float dist = Vector3.Distance(searchPosition, col.bounds.center);
                    if (dist < minDist)
                    {
                        minDist = dist;
                        closestShelf = col.transform;
                    }
                }
            }
            return closestShelf;
        }

        private Vector3 GetDockingNormal(Transform targetTrans, Vector3 targetPos, Transform shelfTrans)
        {
            Vector3 candidateAxis;
            if (shelfTrans != null)
            {
                
                
                float dot = Vector3.Dot(transform.position - targetPos, shelfTrans.right);
                candidateAxis = shelfTrans.right * (dot >= 0f ? 1f : -1f);
                candidateAxis.y = 0;
                candidateAxis = candidateAxis.normalized;

                
                Vector3 snapped;
                if (Mathf.Abs(candidateAxis.x) >= Mathf.Abs(candidateAxis.z))
                    snapped = new Vector3(Mathf.Sign(candidateAxis.x), 0f, 0f);
                else
                    snapped = new Vector3(0f, 0f, Mathf.Sign(candidateAxis.z));

                Debug.Log($"[RobotPathfinder] Docking normal resolved from Shelf X-axis: {snapped} (shelf={shelfTrans.name})");
                return snapped;
            }
            else
            {
                
                
                if (targetTrans != null)
                {
                    candidateAxis = -targetTrans.right;
                }
                else
                {
                    
                    candidateAxis = (transform.position - targetPos);
                }
                
                candidateAxis.y = 0;
                candidateAxis = candidateAxis.normalized;

                
                Vector3 snapped;
                if (Mathf.Abs(candidateAxis.x) >= Mathf.Abs(candidateAxis.z))
                    snapped = new Vector3(Mathf.Sign(candidateAxis.x), 0f, 0f);
                else
                    snapped = new Vector3(0f, 0f, Mathf.Sign(candidateAxis.z));

                Debug.Log($"[RobotPathfinder] Docking normal resolved from target transform: {snapped}");
                return snapped;
            }
        }

        
        
        
        
        
        private Vector3 BuildSafeParkingPosFromShelf(Vector3 cargoCenter, Vector3 normal, Transform shelfTrans, float offsetDist)
        {
            Vector3 parkingPos = cargoCenter;
            if (shelfTrans != null)
            {
                Collider shelfCol = shelfTrans.GetComponentInChildren<BoxCollider>();
                if (shelfCol == null)
                {
                    shelfCol = shelfTrans.GetComponentInChildren<Collider>();
                }
                if (shelfCol != null)
                {
                    Bounds b = shelfCol.bounds;
                    
                    
                    if (normal.x > 0.5f)
                    {
                        parkingPos.x = b.max.x + offsetDist;
                    }
                    else if (normal.x < -0.5f)
                    {
                        parkingPos.x = b.min.x - offsetDist;
                    }
                    else if (normal.z > 0.5f)
                    {
                        parkingPos.z = b.max.z + offsetDist;
                    }
                    else if (normal.z < -0.5f)
                    {
                        parkingPos.z = b.min.z - offsetDist;
                    }
                    return parkingPos;
                }
            }
            
            return cargoCenter + normal * offsetDist;
        }

        
        
        
        
        
        private Vector3 MovedAlongDockingAxis(Vector3 currentPos, Vector3 cargoCenter, Vector3 normal)
        {
            
            float cargoDepth = Vector3.Dot(cargoCenter, normal);

            
            Vector3 dest = currentPos;
            dest.x += normal.x * (cargoDepth - Vector3.Dot(currentPos, normal));
            dest.z += normal.z * (cargoDepth - Vector3.Dot(currentPos, normal));
            return dest;
        }

        private Vector3 GetObjectCenter(GameObject go)
        {
            if (go == null) return Vector3.zero;

            Renderer[] renderers = go.GetComponentsInChildren<Renderer>();
            if (renderers != null && renderers.Length > 0)
            {
                Bounds bounds = renderers[0].bounds;
                for (int i = 1; i < renderers.Length; i++)
                {
                    bounds.Encapsulate(renderers[i].bounds);
                }
                return bounds.center;
            }

            Collider[] colliders = go.GetComponentsInChildren<Collider>();
            if (colliders != null && colliders.Length > 0)
            {
                Bounds bounds = colliders[0].bounds;
                for (int i = 1; i < colliders.Length; i++)
                {
                    bounds.Encapsulate(colliders[i].bounds);
                }
                return bounds.center;
            }

            return go.transform.position;
        }

        private void SetState(RobotState newState)
        {
            state = newState;
            pathRequested = false;
            pathRetryTimer = 0f;
            pathFailureCount = 0;
            hasRouteTarget = false;
            currentWaitPos = Vector3.zero;

            
            if (robotTwin != null)
            {
                switch (state)
                {
                    case RobotState.AvailableAtHome:
                        robotTwin.currentTask = "Available at Home";
                        break;
                    case RobotState.NavigatingToPackage:
                        robotTwin.currentTask = $"Navigating to Package {targetCardboxID}";
                        break;
                    case RobotState.AligningForPickup:
                        robotTwin.currentTask = "Aligning for Package Pickup";
                        break;
                    case RobotState.LiftingToSourceHeight:
                        robotTwin.currentTask = "Lifting to Retrieve Box (Outside)";
                        break;
                    case RobotState.DockingIntoSourceShelf:
                        robotTwin.currentTask = "Docking into Shelf";
                        break;
                    case RobotState.GrabbingCargo:
                        robotTwin.currentTask = "Grabbing Stack";
                        break;
                    case RobotState.UndockingFromSourceShelf:
                        robotTwin.currentTask = "Undocking from Shelf";
                        break;
                    case RobotState.LoweringToGroundWithCargo:
                        robotTwin.currentTask = "Lowering Robot (Loaded)";
                        break;
                    case RobotState.NavigatingToDropoff:
                        robotTwin.currentTask = $"Navigating to Conveyor";
                        break;
                    case RobotState.AligningForDropoff:
                        robotTwin.currentTask = "Aligning for Conveyor Dropoff";
                        break;
                    case RobotState.LiftingToDestHeight:
                        robotTwin.currentTask = "Lifting to Place Box (Outside)";
                        break;
                    case RobotState.DockingIntoDestShelf:
                        robotTwin.currentTask = "Docking into Shelf (Deliver)";
                        break;
                    case RobotState.PlacingCargo:
                        robotTwin.currentTask = "Placing Stack";
                        break;
                    case RobotState.UndockingFromDestShelf:
                        robotTwin.currentTask = "Undocking from Shelf (Deliver)";
                        break;
                    case RobotState.LoweringEmptyToGround:
                        robotTwin.currentTask = "Lowering Robot (Empty)";
                        break;
                    case RobotState.NavigatingHome:
                        robotTwin.currentTask = "Returning Home";
                        break;
                    case RobotState.WaitingForChargingQueue:
                        robotTwin.currentTask = "Waiting for Charger";
                        break;
                    case RobotState.NavigatingToChargingStation:
                        robotTwin.currentTask = "Routing to Charger";
                        break;
                    case RobotState.Charging:
                        robotTwin.currentTask = "Charging";
                        break;
                    case RobotState.Completed:
                        robotTwin.currentTask = "Available at Home";
                        break;
                    case RobotState.AvailableForAssignment:
                        robotTwin.currentTask = "Available for Assignment";
                        break;
                    case RobotState.LeavingChargingStation:
                        robotTwin.currentTask = "Leaving Charging Station";
                        break;
                }
            }
        }

        public bool IsAvailableForAssignment => state == RobotState.AvailableAtHome || state == RobotState.AvailableForAssignment || state == RobotState.NavigatingHome;

        public bool HasActivePackageTask =>
            targetCardbox != null || cargo != null ||
            (state >= RobotState.NavigatingToPackage && state <= RobotState.LoweringEmptyToGround);

        public bool CanAcceptNewPackage =>
            !HasActivePackageTask &&
            (IsAvailableForAssignment ||
             state == RobotState.WaitingForChargingQueue ||
             state == RobotState.NavigatingToChargingStation ||
             state == RobotState.Charging);

        public void SetAvoidancePriority(int priority)
        {
            if (navAgent != null) navAgent.avoidancePriority = Mathf.Clamp(priority, 0, 99);
        }

        public float EstimateShortestPathDistance(Vector3 target)
        {
            if (navAgent == null) return float.PositiveInfinity;
            if (!NavMesh.SamplePosition(transform.position, out NavMeshHit startHit, 3f, NavMesh.AllAreas)) return float.PositiveInfinity;
            if (!NavMesh.SamplePosition(target, out NavMeshHit hit, 3f, NavMesh.AllAreas)) return float.PositiveInfinity;
            NavMeshPath path = new NavMeshPath();
            if (!NavMesh.CalculatePath(startHit.position, hit.position, NavMesh.AllAreas, path) || path.status != NavMeshPathStatus.PathComplete || path.corners == null || path.corners.Length < 2) return float.PositiveInfinity;
            float distance = 0f;
            for (int i = 1; i < path.corners.Length; i++) distance += Vector3.Distance(path.corners[i - 1], path.corners[i]);
            return distance;
        }

        public void MarkAvailableForAssignment()
        {
            targetCardbox = null;
            targetCardboxID = string.Empty;
            destinationTransform = null;
            cargo = null;
            SetAgentActive(true);
            SetState(RobotState.AvailableForAssignment);
        }

        public void ReturnHome()
        {
            targetCardbox = null;
            targetCardboxID = string.Empty;
            destinationTransform = null;
            float distance = Vector2.Distance(new Vector2(transform.position.x, transform.position.z), new Vector2(homePosition.x, homePosition.z));
            if (distance <= 0.25f)
            {
                SetAgentActive(false);
                transform.rotation = homeRotation;
                SetState(RobotState.AvailableAtHome);
                return;
            }
            SetAgentActive(true);
            navAgent.stoppingDistance = 0.1f;
            SetState(RobotState.NavigatingHome);
        }

        public void CancelCurrentTaskAndReturn()
        {
            if (cargo != null)
            {
                cargo.transform.SetParent(originalCargoParent, true);
                cargo.transform.position = originalCargoPosition;
                cargo.transform.rotation = originalCargoRotation;
                foreach (var cb in cargo.GetComponentsInChildren<Cardbox>()) cb.pickupAssigned = false;
            }
            cargo = null;
            targetCardbox = null;
            targetCardboxID = string.Empty;
            destinationTransform = null;
            if (robotTwin != null && robotTwin.battery < 10f && robotTwin.chargingStation != null) TriggerChargingSequence(robotTwin.chargingStation);
            else ReturnHome();
        }

        private bool EnsureShortestPath(Vector3 target)
        {
            if (navAgent == null || !navAgent.enabled || !navAgent.isOnNavMesh) return false;
            bool sameTarget = hasRouteTarget && Vector3.SqrMagnitude(lastRouteTarget - target) <= 0.01f;
            if (pathRequested && sameTarget && (navAgent.pathPending || (navAgent.hasPath && navAgent.pathStatus == NavMeshPathStatus.PathComplete))) return true;
            if (!NavMesh.SamplePosition(target, out NavMeshHit hit, 3f, NavMesh.AllAreas)) { RegisterPathFailure($"No NavMesh near {target}"); return false; }
            NavMeshPath path = new NavMeshPath();
            if (!NavMesh.CalculatePath(transform.position, hit.position, NavMesh.AllAreas, path) || path.status != NavMeshPathStatus.PathComplete || path.corners == null || path.corners.Length < 2) { RegisterPathFailure($"No complete path to {target}"); return false; }
            navAgent.SetPath(path);
            pathRequested = true;
            pathRetryTimer = 0f;
            lastRouteTarget = target;
            hasRouteTarget = true;
            return true;
        }

        private void RegisterPathFailure(string reason)
        {
            pathRetryTimer += Time.deltaTime;
            if (pathRetryTimer < 1f) return;
            pathRetryTimer = 0f;
            pathFailureCount++;
            pathRequested = false;
            hasRouteTarget = false;
            if (pathFailureCount >= MaxPathAttempts) HandlePathFailure(reason);
        }

        private void HandlePathFailure(string reason)
        {
            Debug.LogWarning($"[RobotPathfinder] {robotTwin?.robotID ?? name} path failed: {reason}");
            bool jobRoute = state == RobotState.NavigatingToPackage || state == RobotState.NavigatingToDropoff;
            pathFailureCount = 0;
            pathRequested = false;
            hasRouteTarget = false;
            if (jobRoute)
            {
                
                if (navAgent != null && navAgent.enabled && navAgent.isOnNavMesh)
                    navAgent.ResetPath();
                return;
            }
            if (state == RobotState.NavigatingToChargingStation) SetState(RobotState.WaitingForChargingQueue);
        }
        private void UpdateStuckDetection()
        {
            bool navigating = state == RobotState.NavigatingToPackage || state == RobotState.NavigatingToDropoff || state == RobotState.NavigatingHome || state == RobotState.NavigatingToChargingStation;
            if (!navigating || navAgent == null || !navAgent.enabled || navAgent.pathPending) { stuckTimer = 0f; return; }
            if (navAgent.velocity.sqrMagnitude >= 0.01f) { stuckTimer = 0f; return; }
            stuckTimer += Time.deltaTime;
            if (stuckTimer < 4f) return;
            stuckTimer = 0f;
            pathFailureCount++;
            pathRequested = false;
            hasRouteTarget = false;
            if (navAgent.isOnNavMesh)
                navAgent.ResetPath();
            else if (NavMesh.SamplePosition(transform.position, out NavMeshHit hit, 1.5f, NavMesh.AllAreas))
                navAgent.Warp(hit.position);
            if (pathFailureCount >= MaxPathAttempts) HandlePathFailure($"No progress while in {state}");
        }
        private string GetTaskNameForState(RobotState s)
        {
            switch (s)
            {
                case RobotState.AvailableAtHome:
                    return "Available at Home";
                case RobotState.NavigatingToPackage:
                    return $"Navigating to Package {targetCardboxID}";
                case RobotState.AligningForPickup:
                    return "Aligning for Package Pickup";
                case RobotState.LiftingToSourceHeight:
                    return "Lifting to Retrieve Box (Outside)";
                case RobotState.DockingIntoSourceShelf:
                    return "Docking into Shelf";
                case RobotState.GrabbingCargo:
                    return "Grabbing Stack";
                case RobotState.UndockingFromSourceShelf:
                    return "Undocking from Shelf";
                case RobotState.LoweringToGroundWithCargo:
                    return "Lowering Robot (Loaded)";
                case RobotState.WaitingForDestinationQueue:
                    return "Waiting for Dest Queue";
                case RobotState.NavigatingToDropoff:
                    return "Navigating to Conveyor";
                case RobotState.AligningForDropoff:
                    return "Aligning for Conveyor Dropoff";
                case RobotState.LiftingToDestHeight:
                    return "Lifting to Place Box (Outside)";
                case RobotState.DockingIntoDestShelf:
                    return "Docking into Shelf (Deliver)";
                case RobotState.PlacingCargo:
                    return "Placing Stack";
                case RobotState.UndockingFromDestShelf:
                    return "Undocking from Shelf (Deliver)";
                case RobotState.LoweringEmptyToGround:
                    return "Lowering Robot (Empty)";
                case RobotState.NavigatingHome:
                    return "Returning Home";
                case RobotState.WaitingForChargingQueue:
                    return "Waiting for Charger";
                case RobotState.NavigatingToChargingStation:
                    return "Routing to Charger";
                case RobotState.Charging:
                    return "Charging";
                case RobotState.LeavingChargingStation:
                    return "Leaving Charging Station";
                case RobotState.Completed:
                    return "Available at Home";
                case RobotState.AvailableForAssignment:
                    return "Available for Assignment";
                default:
                    return "Unknown";
            }
        }
    }
}
