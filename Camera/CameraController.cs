﻿using UnityEngine;
using System.Collections;
using System.Collections.Generic;
using UnityEngine.Networking;

public class CameraController : MonoBehaviour
{
    #region Values
    /// <summary>
    /// Defines the controlling player of the camera controller.
    /// </summary>
    public LocalPlayer LocalPlayer;

    /// <summary>
    /// Defines the camera component attached to the camera controller's game object.
    /// </summary>
    [HideInInspector]
    public Camera Camera;

    /// <summary>
    /// Defines the deduction of the field of view when there are two local players.
    /// </summary>
    public float TwoByFovReduction;

    [HideInInspector]
    public bool Zooming;
    public float DefaultFieldOfView;
    public float ZoomedFieldOfView;
    public float ZoomSpeed;

    public float ScreenShakeSpeed;

    /// <summary>
    /// Defines the speed in which the camera rotates.
    /// </summary>
    public float RotationSpeed;

    [ValueReference("auto_spawn_duration")]
    public float AutoSpawnDuration;

    /// <summary>
    /// Defines the radius in-which the camera is allowed to select a prospect inanimate object.
    /// </summary>
    [Space(15)]
    [Header("Selection Defaults")]
    public float SelectionRadius;
    /// <summary>
    /// Defines the distance in-which the camera is allowed to select a prospect inanimate object.
    /// </summary>
    public float SelectionDistance;
    public enum SelectState
    {
        None,
        Allowed,
        Denied
    }
    /// <summary>
    /// Defines the state of the current attachment proposal.
    /// </summary>
    [HideInInspector]
    [ValueReference("selection_state")]
    public SelectState SelectionState;

    /// <summary>
    /// Defines the name of the inanimate object that the camera is currently viewing for attachment.
    /// </summary>
    [HideInInspector]
    [ValueReference("prospect_attach_name")]
    public string ProspectAttachName;

    /// <summary>
    /// Defines the classification of the inanimate object that the camera is currently viewing for attachment.
    /// </summary>
    [HideInInspector]
    [ValueReference("prospect_attach_class")]
    public InanimateObject.Classification ProspectAttachClass;
    private InanimateObject _prospectSelection;

    /// <summary>
    /// Defines the speed in which the camera moves when in free cam.
    /// </summary>
    public float SelectingFlyingSpeed;
    public float SelectingFlyingBoostedSpeed;
    /// <summary>
    /// Defines the constraints of the camera controllers rotation along the X axis while in free cam.
    /// </summary>
    public Vector2 SelectingOrbitPitchRange;

    /// <summary>
    /// Defines the constraints of the camera controllers rotation along the X axis while attached.
    /// </summary>
    [Space(15)]
    [Header("Attached Defaults")]
    public Vector2 AttachedOrbitPitchRange;

    /// <summary>
    /// Defines the distance forward from the collision point the camera will rest at.
    /// </summary>
    public float AttachedCollisionOffset;
    public float AttachedCollisionDistanceAddition;
    /// <summary>
    /// Determines which layers the camera's collision ignores.
    /// </summary>
    public LayerMask AttachedCollisionIgnoredLayers;

    public float AreaIsolatorFadeRate;

    public float TurningScale = 1;

    public bool AwaitingAttachment;

    public class ScreenShake
    {
        public float Duration;
        public float DurationRemaining;
        public float Intensity;
        public AnimationCurve LifetimeIntensity;

        public ScreenShake(float duration, float intensity, AnimationCurve lifetimeIntensity)
        {
            Duration = duration;
            DurationRemaining = duration;
            Intensity = intensity;
            LifetimeIntensity = lifetimeIntensity;
        }
    }
    private List<ScreenShake> _screenShakes = new List<ScreenShake>();

    private bool _attached;
    // A collection of previously generated regular polys.
    private static Dictionary<int, Vector2[]> _storedRegularPolys = new Dictionary<int, Vector2[]>();

    // Defines the rigid body component of the game object.
    private Rigidbody _rigidBody;
    // Defines the sphere collider component of the game object.
    private SphereCollider _sphereCollider;

    private Material _isolatorMaterial;
    private Color _isolatorMaterialStartingColor;
    private float _isolatorColorTime;
    #endregion

    #region Unity Functions
    private void Awake()
    {
        Initialize();
        SetAutoAttachDuration();
    }

    private void Update()
    {
        UpdateFieldOfView();
        UpdateCameraShake();
        UpdateAutoAttach();
    }
    
    public void OnPreRender()
    {
        if (_prospectSelection != null)
            _prospectSelection.ToggleProspectColor(true);
        
        RenderAreaIsolatorMaterial();
        SetIsolatorMaterial();
        RenderBombObjectives();
    }
    public void OnPostRender()
    {
        if (_prospectSelection != null)
            _prospectSelection.ToggleProspectColor(false);
    }
    #endregion

    #region Functions
    private void Initialize()
    {
        // Defines various defaults within the camera controller.
        _rigidBody = this.gameObject.GetComponent<Rigidbody>();
        _sphereCollider = this.gameObject.GetComponent<SphereCollider>();
        Camera = this.gameObject.GetComponent<Camera>();

        _isolatorMaterial = new Material(Globals.Instance.AreaIsolatorMaterial);
        _isolatorMaterialStartingColor = _isolatorMaterial.GetColor("_TintColor");

        SetCameraAttributes(false);
    }

    /// <summary>
    /// Moves the camera controller along the input direction if it is currently no attached to an inanimate object.
    /// </summary>
    /// <param name="input"> The direction in-which to move</param>
    public void Move(Vector2 input, bool boosting)
    {
        // Set camera velocity based on input
        float speed = boosting ? SelectingFlyingBoostedSpeed : SelectingFlyingSpeed;
        Vector3 horizontalVelocity = ((this.transform.right * input.x) + (this.transform.forward * input.y)) * speed * Time.deltaTime;
        _rigidBody.velocity = horizontalVelocity;
    }

    /// <summary>
    /// Rotates the camera along the input direction.
    /// </summary>
    /// <param name="input">The direction in-which to rotate.</param>
    public void Look(Vector2 input)
    {
        // Define rotation speed, define look inversion then create new rotation
        float rotationSpeed = LocalPlayer.InanimateObject != null && LocalPlayer.InanimateObject.Movement.Lunging ?
        RotationSpeed * Globals.Instance.InanimateDefaults.Light.Lunge.CameraOrbitSensitivity : RotationSpeed * GameManager.GetLookSensitivity(LocalPlayer.Profile.LookSensitivity);
        rotationSpeed *= TurningScale;

        Vector3 newRotation = this.transform.eulerAngles + new Vector3(input.y * rotationSpeed, input.x * rotationSpeed, 0) * Time.deltaTime;

        // Correct new rotation in case its beyond the clamp bounds and apply rotation
        Vector2 pitchClamp = LocalPlayer.InanimateObject != null ? AttachedOrbitPitchRange : SelectingOrbitPitchRange;
        newRotation.x = ClampAngle(newRotation.x, pitchClamp);
        this.transform.eulerAngles = newRotation;


        if (LocalPlayer.InanimateObject != null)
        {
            int layerBackup = LocalPlayer.InanimateObject.gameObject.layer;
            LocalPlayer.InanimateObject.gameObject.layer = 2;

            // If attached; position camera to offset and update camera collision

            // Position camera at offset
            Vector3 relativeFollowOffset = this.transform.rotation * LocalPlayer.InanimateObject.CameraFollowOffset;
            this.transform.position = LocalPlayer.InanimateObject.transform.position + relativeFollowOffset;

            // Reposition camera if something is in its way
            Vector3 castDirection = (this.transform.position - LocalPlayer.InanimateObject.transform.position).normalized;
            float castDistance = Vector3.Distance(this.transform.position, LocalPlayer.InanimateObject.transform.position) + AttachedCollisionDistanceAddition;
            RaycastHit raycastHit;
            bool hit = Physics.Raycast(LocalPlayer.InanimateObject.transform.position, castDirection, out raycastHit, castDistance, ~AttachedCollisionIgnoredLayers);
            if (hit && raycastHit.collider != null)
                this.transform.position = raycastHit.point - (raycastHit.normal * AttachedCollisionOffset);

            LocalPlayer.InanimateObject.gameObject.layer = layerBackup;
        }
    }

    private void UpdateFieldOfView()
    {
        float targetFieldOfView = Zooming ? ZoomedFieldOfView : DefaultFieldOfView;
        Camera.fieldOfView = Mathf.MoveTowards(Camera.fieldOfView, targetFieldOfView, Time.deltaTime * ZoomSpeed);
    }

    private void UpdateCameraShake()
    {
        float intensity = 0;
        for (int i = 0; i < _screenShakes.Count; i++)
        {
            ScreenShake screenShake = _screenShakes[i];
            // Scale intensity
            float scaleTime = screenShake.DurationRemaining / screenShake.Duration;
            float scale = screenShake.LifetimeIntensity.Evaluate(scaleTime);
            intensity += GetRandomDirection() * screenShake.Intensity * scale;

            // Remove screen shake if duration has become durated
            screenShake.DurationRemaining -= Time.deltaTime;
            if (screenShake.DurationRemaining <= 0)
            {
                _screenShakes.Remove(screenShake);
                i--;
            }
        }

        // Apply rotation
        Vector3 targetRotation = this.transform.eulerAngles;
        targetRotation.z = intensity;
        this.transform.rotation = Quaternion.RotateTowards(this.transform.rotation, Quaternion.Euler(targetRotation), Time.deltaTime * (ScreenShakeSpeed + intensity));
    }
    public void AddShake(float duration, float intensity, AnimationCurve lifetimeIntensity)
    {
        _screenShakes.Add(new ScreenShake(duration, intensity, lifetimeIntensity));
    }
    public void AddShake(PhysicalEffect physicalEffect)
    {
        _screenShakes.Add(new ScreenShake(physicalEffect.ScreenShake.Duration, physicalEffect.ScreenShake.Intensity, physicalEffect.ScreenShake.IntensityOverLifetime));
    }


    private void RenderAreaIsolatorMaterial()
    {
        _isolatorColorTime = Mathf.Min(_isolatorColorTime + Time.deltaTime * AreaIsolatorFadeRate, 1);
        Color color = _isolatorMaterialStartingColor;
        if (_attached)
            color = Color.Lerp(_isolatorMaterialStartingColor, Color.black, _isolatorColorTime);
        else
            color = Color.Lerp(Color.black, _isolatorMaterialStartingColor, _isolatorColorTime);

        _isolatorMaterial.SetColor("_TintColor", color);
    }

    private void RenderBombObjectives()
    {
        if (GameManager.Instance.Game.GameType.Mode != GameManager.GameAspects.GameSettings.GameMode.Bomb)
            return;

        Territory[] territories = FindObjectsOfType<Territory>();
        foreach (Territory territory in territories)
            territory.gameObject.SetActive(territory.TeamId != LocalPlayer.Profile.TeamId);
    }


    public void HighlightProspect()
    {
        // Sets the details of the prospect inanimate object, and determines if its allowed to be selected.
        // Populate name, class and selection state
        ProspectAttachName = _prospectSelection.Name;
        ProspectAttachClass = _prospectSelection.Class;

        // Determine selection state, return denied if the prospect selections class is disabled in the gametype
        SelectionState = IsClassEnabled(_prospectSelection) ? CameraController.SelectState.Allowed : CameraController.SelectState.Denied;
    }
    public void UnHighlightProspect()
    {
        // Clears the details of the previous prospect selection.
        _prospectSelection = null;
        SelectionState = CameraController.SelectState.None;
        ProspectAttachClass = InanimateObject.Classification.None;
        ProspectAttachName = string.Empty;
    }
    /// <summary>
    /// Displays selection state and allows selection of an inanimate object when unattached.
    /// </summary>
    /// <param name="input"></param>
    public void ProspectSelection(bool input)
    {
        UnHighlightProspect();
        // Cast ahead to determine if there is an object to attach to
        RaycastHit raycastHit;
        // bool hit = CastRadiusFromCenter(Camera, SelectionRadius * LocalPlayer.HeadsUpDisplay.ScaleFactor, 6, SelectionDistance, out raycastHit, 0);

        bool hit = Target(SelectionRadius * LocalPlayer.HeadsUpDisplay.ScaleFactor, SelectionDistance, out raycastHit, 0);

        _prospectSelection = null;

        if (hit && raycastHit.collider != null)
            _prospectSelection = raycastHit.collider.gameObject.GetComponent<InanimateObject>();

        if (_prospectSelection == null)
        {
            Collider[] localColliders = Physics.OverlapSphere(this.transform.position, _sphereCollider.radius);
            foreach (Collider localCollider in localColliders)
            {
                _prospectSelection = localCollider.GetComponent<InanimateObject>();
                if (_prospectSelection != null)
                    break;
            }
        }
        if (_prospectSelection != null && _prospectSelection.Controlled)
            _prospectSelection = null;

        if (_prospectSelection != null)
        {
            HighlightProspect();

            // Select if inputting, and allowed
            if (input && SelectionState == SelectState.Allowed)
                SelectProspect(_prospectSelection);
        }
    }
    private void SelectProspect(InanimateObject inanimateObject)
    {
        if (AwaitingAttachment)
            return;

        if (NetworkClient.active)
        {
            AwaitingAttachment = true;
            NetworkIdentity inan = inanimateObject.gameObject.GetComponent<NetworkIdentity>();
            NetworkSessionNode.Instance.CmdRequestAttach(inan, LocalPlayer.Profile.ControllerId, LocalPlayer.Profile.GamerId, LocalPlayer.Profile.TeamId);
        }
        else
        {
            // Selects a prospect inanimate object.
            UnHighlightProspect();
            SetCameraAttributes(true);
            LocalPlayer.AttachTo(inanimateObject);
        }
    }
    public bool IsClassEnabled(InanimateObject inanimateObject)
    {
        bool enabled = true;
        if (inanimateObject.Class == InanimateObject.Classification.Throwy && !GameManager.Instance.Game.GameType.ThrowyAllowed)
            enabled = false;
        else if (inanimateObject.Class == InanimateObject.Classification.Squirty && !GameManager.Instance.Game.GameType.SquirtyAllowed)
            enabled = false;
        else if (inanimateObject.Class == InanimateObject.Classification.Smashy && !GameManager.Instance.Game.GameType.FloppyAllowed)
            enabled = false;
        return enabled;
    }

    public void AutoAttach()
    {
        LocalPlayer.Respawn();
        for (int j = 0; j < Globals.Instance.Containers.Objects.childCount; j++)
        {
            InanimateObject inanimateObject = Globals.Instance.Containers.Objects.GetChild(j).gameObject.GetComponent<InanimateObject>();

            if (!inanimateObject.Controlled && IsClassEnabled(inanimateObject))
            {
                RaycastHit raycastHit;
                Vector3 direction = inanimateObject.transform.position - this.transform.position;
                bool hit = Physics.Raycast(this.transform.position, direction, out raycastHit, Mathf.Infinity);
                if (hit && raycastHit.collider.gameObject == inanimateObject.gameObject)
                {
                    SelectProspect(inanimateObject);
                    return;
                }
            }
        }
    }
    public void UpdateAutoAttach()
    {
        if (AwaitingAttachment)
            return;
        if (!_attached)
        {
            if (AutoSpawnDuration > 0)
                AutoSpawnDuration -= Time.deltaTime;
            else
                AutoAttach();
        }
    }
    public void SetAutoAttachDuration()
    {
        AutoSpawnDuration = GameManager.Instance.Game.GameType.AutoAttachDuration;
    }

    public void SetIsolatorMaterial()
    {
        for (int i = 0; i < Globals.Instance.Containers.AreaIsolators.childCount; i++)
        {
            Transform transform = Globals.Instance.Containers.AreaIsolators.GetChild(i);
            Renderer renderer = transform.GetComponent<Renderer>();
            renderer.sharedMaterial = _isolatorMaterial;
        }
    }


    public void SetCameraAttributes(bool attached)
    {
        if (!attached)
        {
            _screenShakes.Clear();
            TurningScale = 1;
        }
        if (_attached != attached)
        {
            _isolatorColorTime = 0;
            SetAutoAttachDuration();
        }

        _attached = attached;
        Zooming = false;
        _sphereCollider.enabled = !attached;
    }
    /// <summary>
    /// Defines the size and position of the camera's rect based on the quantity of local players.
    /// </summary>
    /// <param name="localPlayerCount"> The quantity of local players.</param>
    /// <param name="index"> The index of the camera's player.</param>
    public void AppropriateRect(int localPlayerCount, int index)
    {
        if (localPlayerCount == 1)
            return;
        // If the player count is two, set full width, half height, and stack vertically
        // If the player count is three, set quarter size, if the index is less that two stack horizontally at the top of the screen
        // and if the index is three center at the bottom
        // If the player count is four, set quarter size, if the index is less than two stack horizontally at the top of the screen
        // otherwise stack horizontally at the bottom of the screen
        float x = 0;
        float y = index < 2 ? 0.5f : 0;
        if (localPlayerCount == 2)
            y = index == 0 ? 0.5f : 0;
        else if (localPlayerCount == 3)
            x = index < 2 ? (index % 2 == 0 ? 0 : 0.5f) : 0.25f;
        else
            x = index % 2 == 0 ? 0 : 0.5f;
        Camera.rect = new Rect(x, y, localPlayerCount == 2 ? 1 : 0.5f, 0.5f);
        // Reduce field of view if the player count is two
        DefaultFieldOfView -= localPlayerCount == 2 ? TwoByFovReduction : 0;
        ZoomedFieldOfView -= localPlayerCount == 2 ? TwoByFovReduction : 0;
        Camera.fieldOfView = DefaultFieldOfView;
    }


    public bool Target(float targetingRadius, float distance, out RaycastHit raycastHit, LayerMask ignoredLayers)
    {
        raycastHit = new RaycastHit();

        // Calculate screen center
        Vector2 screenCenter = (new Vector2(Camera.pixelRect.width, Camera.pixelRect.height) / 2) + Camera.pixelRect.position;

        // Create radius points
        Vector2[] radiusPoints = CreateRegularPoly2D(8);
        Vector2[] relativeRadiusPoints = new Vector2[8];
        for (int i = 0; i < radiusPoints.Length; i++)
            relativeRadiusPoints[i] = screenCenter + radiusPoints[i] * targetingRadius;

        // Loop through each inanimate object to get its targeting boxes
        for (int j = 0; j < Globals.Instance.Containers.Objects.childCount; j++)
        {
            InanimateObject inanimateObject = Globals.Instance.Containers.Objects.GetChild(j).gameObject.GetComponent<InanimateObject>();

            foreach (InanimateObject.TargetingBox targetingBox in inanimateObject.TargetingBoxes)
            {
                // Define the points of the target
                Vector3[] targetWorldPoints = null;
                Vector3[] targetScreenPoints = null;
                GetTargetPoints(targetingBox, inanimateObject.transform, out targetWorldPoints, out targetScreenPoints);

                // Check if the reticle and the target box overlap
                if (TargetInRadius(screenCenter, targetingRadius, relativeRadiusPoints, distance, targetWorldPoints, targetScreenPoints, inanimateObject, out raycastHit, ~ignoredLayers))
                    return true;
            }
        }

        return Physics.Raycast(Camera.ScreenPointToRay(screenCenter), out raycastHit, distance, ~ignoredLayers);
    }
    private bool TargetInRadius(Vector2 screenCenter, float radius, Vector2[] radiusPoints, float maxDistance, Vector3[] targetWorldPoints, Vector3[] targetScreenPoints, InanimateObject objectProperties, out RaycastHit raycastHit, LayerMask ignoredLayers)
    {
        raycastHit = new RaycastHit();

        if (radius == 0)
            return false;

        // Check distance
        float distanceToTarget = Vector3.Distance(this.transform.position, objectProperties.transform.position);
        bool foundTarget = distanceToTarget <= maxDistance;

        if (!foundTarget)
            return false;

        int worldPointIndex = 0;

        // Check if a point from the targeting box is in the radius
        foundTarget = PointsInRadius(screenCenter, radius, targetScreenPoints, out worldPointIndex);

        if (foundTarget)
        {
            // If a point of the target is within the radius cast to it, if hit 
            foundTarget = false;
            if (Physics.Raycast(this.transform.position, targetWorldPoints[worldPointIndex] - this.transform.position, out raycastHit, maxDistance, ignoredLayers))
                foundTarget = raycastHit.collider.gameObject == objectProperties.gameObject;
        }
        else
        {
            // Check if a radius point is within the targeting box
            foreach (Vector2 radiusPoint in radiusPoints)
            {
                foundTarget = PointInVolume(radiusPoint, targetScreenPoints);
                if (foundTarget)
                {
                    Ray screenPointToRay = Camera.ScreenPointToRay(radiusPoint);
                    if (Physics.Raycast(screenPointToRay, out raycastHit, maxDistance, ignoredLayers))
                    {
                        foundTarget = raycastHit.collider.gameObject == objectProperties.gameObject;
                        if (foundTarget)
                            break;
                    }
                }
            }
        }

        return foundTarget;
    }
    private void GetTargetPoints(InanimateObject.TargetingBox targetingBox, Transform targetTransform, out Vector3[] targetWorldPoints, out Vector3[] targetScreenPoints)
    {
        targetWorldPoints = new Vector3[8];
        targetScreenPoints = new Vector3[8];

        Vector3 halfExtends = targetingBox.Size / 2;
        Vector3 backOffset = Vector3.forward * targetingBox.Size.z;
        // Create box points
        targetWorldPoints[0] = targetingBox.Center + new Vector3(halfExtends.x, halfExtends.y, halfExtends.z);
        targetWorldPoints[1] = targetingBox.Center + new Vector3(-halfExtends.x, halfExtends.y, halfExtends.z);
        targetWorldPoints[2] = targetingBox.Center + new Vector3(halfExtends.x, -halfExtends.y, halfExtends.z);
        targetWorldPoints[3] = targetingBox.Center + new Vector3(-halfExtends.x, -halfExtends.y, halfExtends.z);
        targetWorldPoints[4] = targetWorldPoints[0] - backOffset;
        targetWorldPoints[5] = targetWorldPoints[1] - backOffset;
        targetWorldPoints[6] = targetWorldPoints[2] - backOffset;
        targetWorldPoints[7] = targetWorldPoints[3] - backOffset;

        for (int e = 0; e < 8; e++)
        {
            // Create relative points 
            targetWorldPoints[e] = (targetTransform.rotation * Quaternion.Euler(targetingBox.Rotation)) * Vector3.Scale(targetWorldPoints[e], targetTransform.localScale) + targetTransform.position;
            targetScreenPoints[e] = Camera.WorldToScreenPoint(targetWorldPoints[e]);
        }
    }
    private bool PointsInRadius(Vector2 screenCenter, float radius, Vector3[] targetScreenPoints, out int worldPoint)
    {
        // Find a point within the radius
        worldPoint = 0;
        for (int i = 0; i < targetScreenPoints.Length; i++)
        {
            if (targetScreenPoints[i].z < 0)
                continue;
            if (Vector2.Distance(screenCenter, targetScreenPoints[i]) <= radius)
            {
                worldPoint = i;
                return true;
            }
        }

        return false;
    }
    private bool PointInTriangle(Vector3 point, Vector3 vert0, Vector3 vert1, Vector3 vert2)
    {
        if (vert0.z < 0 || vert1.z < 0 || vert2.z < 0)
            return false;
        
        var s = vert0.y * vert2.x - vert0.x * vert2.y + (vert2.y - vert0.y) * point.x + (vert0.x - vert2.x) * point.y;
        var t = vert0.x * vert1.y - vert0.y * vert1.x + (vert0.y - vert1.y) * point.x + (vert1.x - vert0.x) * point.y;

        if ((s < 0) != (t < 0))
            return false;

        var A = -vert1.y * vert2.x + vert0.y * (vert2.x - vert1.x) + vert0.x * (vert1.y - vert2.y) + vert1.x * vert2.y;
        if (A < 0.0)
        {
            s = -s;
            t = -t;
            A = -A;
        }
        return s > 0 && t > 0 && (s + t) <= A;
    }
    private bool PointInVolume(Vector3 point, Vector3[] targetVerts)
    {
        return PointInTriangle(point, targetVerts[0], targetVerts[1], targetVerts[2]) ||
        PointInTriangle(point, targetVerts[2], targetVerts[3], targetVerts[1]) ||
        PointInTriangle(point, targetVerts[0], targetVerts[4], targetVerts[6]) ||
        PointInTriangle(point, targetVerts[6], targetVerts[2], targetVerts[0]) ||
        PointInTriangle(point, targetVerts[4], targetVerts[5], targetVerts[6]) ||
        PointInTriangle(point, targetVerts[6], targetVerts[7], targetVerts[5]) ||
        PointInTriangle(point, targetVerts[1], targetVerts[5], targetVerts[7]) ||
        PointInTriangle(point, targetVerts[7], targetVerts[3], targetVerts[1]) ||
        PointInTriangle(point, targetVerts[0], targetVerts[1], targetVerts[4]) ||
        PointInTriangle(point, targetVerts[4], targetVerts[5], targetVerts[1]) ||
        PointInTriangle(point, targetVerts[6], targetVerts[7], targetVerts[3]) ||
        PointInTriangle(point, targetVerts[3], targetVerts[2], targetVerts[6]);
    }

    private static Vector2[] CreateRegularPoly2D(int sides)
    {
        // Returns a quantity of points around a circle at an equal distance from on another.
        // If sides are less than 3, return one point
        if (sides < 3)
            return new Vector2[] { Vector2.zero };

        if (_storedRegularPolys.ContainsKey(sides))
            return _storedRegularPolys[sides];

        // The collection of points to be defined and returned
        Vector2[] points = new Vector2[sides];

        // Find each point evenly around a circle
        float thetaDelta = (2f * Mathf.PI) * (1f / sides);
        for (int pointStep = 0; pointStep < sides; pointStep++)
        {
            float iteration = thetaDelta * pointStep;
            Vector3 point = Vector2.zero;
            point.x = Mathf.Cos(iteration);
            point.y = Mathf.Sin(iteration);

            points[pointStep] = point;
        }
        _storedRegularPolys.Add(sides, points);
        return points;
    }

    private int GetRandomDirection()
    {
        int direction = Random.Range(0, 2);
        return direction == 0 ? -1 : 1;
    }


    private static float ClampAngle(float angle, Vector2 minMax)
    {
        // Clamps and angle between a minimum and maximum angle.
        // Make our range between [0-360]
        angle = (360 + (angle % 360)) % 360;
        minMax.x = (360 + (minMax.x % 360)) % 360;
        minMax.y = (360 + (minMax.y % 360)) % 360;

        // If min is less than max
        if (minMax.x <= minMax.y)
            Mathf.Clamp(angle, minMax.x, minMax.y);
        else
        {
            // If in bounds, if not, return
            if (angle >= 0 && angle <= minMax.y)
                return angle;
            else if (angle <= 360 && angle >= minMax.x)
                return angle;
            else
            {
                // Clamp to closest
                if (Mathf.Abs(angle - minMax.y) < Mathf.Abs(angle - minMax.x))
                    return minMax.y;
                else
                    return minMax.x;
            }

        }

        return 0;
    }

    private void DrawTriangle(Vector3 vert0, Vector3 vert1, Vector3 vert2, Color color = new Color())
    {
        if (color == new Color())
            color = Color.red;

        Debug.DrawLine(vert0, vert1, color);
        Debug.DrawLine(vert1, vert2, color);
        Debug.DrawLine(vert2, vert0, color);

    }
    #endregion
}