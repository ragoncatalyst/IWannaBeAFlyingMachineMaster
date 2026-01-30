using System.Collections.Generic;
using UnityEngine;

public class CamaraFollow : MonoBehaviour
{
    [Header("目标设置")]
    [SerializeField] private Transform target; // 要跟随的目标（火箭）
    
    [Header("摄像头设置")]
    [SerializeField] private float baseDistance = 20f;              // 基础距离
    [SerializeField] private float minDistance = 10f;               // 最小距离
    [SerializeField] private float maxDistance = 30f;               // 最大距离
    [SerializeField] private float scrollSensitivity = 2f;          // 滚轮灵敏度
    [SerializeField] private float distanceReturnSpeed = 3f;        // 距离归位基础速度
    [SerializeField] private float maxDistanceReturnSpeed = 10f;    // 距离归位最大速度
    [SerializeField] private float rotationReturnSpeed = 50f;       // 角度归位基础速度（度/秒）
    [SerializeField] private float maxRotationReturnSpeed = 200f;   // 角度归位最大速度（度/秒）
    [SerializeField] private float rotationTransitionTime = 0.3f;   // 角度切换过渡时间
    [SerializeField] private float waitListTimeout = 0.2f;          // 等待列表超时时间（秒）
    
    // 基准摄像头角度偏移（Euler angles）
    private readonly Vector3 baseRotation = new Vector3(30f, 30f, 0f);
    
    private float currentYRotation = 0f;  // 当前Y轴旋转角度
    private float currentDistance = 20f;  // 当前距离
    private bool isTransitioning = false; // 是否正在过渡
    private float transitionTimer = 0f;   // 过渡计时器
    private float startYRotation = 0f;    // 过渡起始Y旋转
    private float targetYRotation = 0f;   // 目标Y轴旋转
    private float debugTimer = 0f;        // 调试输出计时器
    
    // 等待列表
    private class RotationTask
    {
        public float deltaAngle;
        public float addedTime;
        
        public RotationTask(float deltaAngle, float addedTime)
        {
            this.deltaAngle = deltaAngle;
            this.addedTime = addedTime;
        }
    }
    
    private Queue<RotationTask> rotationWaitList = new Queue<RotationTask>();
    private float lastRotationEndTime = 0f;  // 最后一次旋转结束的时间
    
    /// <summary>
    /// 获取当前角度索引（供Movement使用）
    /// 返回最接近的90度整数倍索引（0=0°, 1=90°, 2=180°, 3=270°）
    /// </summary>
    public int GetCurrentAngleIndex()
    {
        float normalizedAngle = (currentYRotation % 360f + 360f) % 360f;
        // 因为旋转方向反了，索引也需要反过来
        int rawIndex = Mathf.RoundToInt(normalizedAngle / 90f) % 4;
        // 反转索引映射：0->0, 1->3, 2->2, 3->1
        return (4 - rawIndex) % 4;
    }

    void Start()
    {
        // 查找目标
        if (target == null)
        {
            GameObject rocket = GameObject.Find("Rocket");
            if (rocket != null)
            {
                target = rocket.transform;
            }
            else
            {
                Movement movement = FindObjectOfType<Movement>();
                if (movement != null)
                {
                    target = movement.transform;
                }
            }
        }
    
        if (target != null)
        {
            // 初始化为0°
            currentYRotation = 0f;
            targetYRotation = 0f;
            currentDistance = baseDistance;
            
            // 初始化摄像头位置
            InitializeCameraPosition();
            
            Debug.Log($"[CamaraFollow] 目标: {target.name}, 位置: {target.position}");
        }
        else
        {
            Debug.LogError("[CamaraFollow] 未找到目标（Rocket）!");
        }
    }
    
    void LateUpdate()
    {
        if (target == null) return;
        
        ProcessWaitList();
        HandleAngleSwitch();
        HandleDistanceControl();
        UpdateCameraPosition();
    }
    
    /// <summary>
    /// 处理等待列表
    /// </summary>
    void ProcessWaitList()
    {
        // 如果不在旋转且等待列表有任务，检查是否可以执行
        if (!isTransitioning && rotationWaitList.Count > 0)
        {
            // 检查最早的任务是否超时
            RotationTask task = rotationWaitList.Peek();
            float taskAge = Time.time - task.addedTime;
            
            if (taskAge <= waitListTimeout)
            {
                // 未超时，执行任务
                rotationWaitList.Dequeue();
                StartTransition(task.deltaAngle);
                Debug.Log($"[CamaraFollow] 从等待列表执行旋转任务: {task.deltaAngle:F0}°, 等待时间: {taskAge:F3}秒");
            }
            else
            {
                // 超时，移除任务
                rotationWaitList.Dequeue();
                Debug.Log($"[CamaraFollow] 移除超时任务: {task.deltaAngle:F0}°, 等待时间: {taskAge:F3}秒");
            }
        }
        
        // 清理所有超时的任务
        while (rotationWaitList.Count > 0)
        {
            RotationTask task = rotationWaitList.Peek();
            float taskAge = Time.time - task.addedTime;
            
            if (taskAge > waitListTimeout)
            {
                rotationWaitList.Dequeue();
                Debug.Log($"[CamaraFollow] 清理超时任务: {task.deltaAngle:F0}°, 等待时间: {taskAge:F3}秒");
            }
            else
            {
                break; // 队列前面的任务未超时，后面的肯定也没超时
            }
        }
    }
    
    /// <summary>
    /// 处理滚轮控制距离
    /// </summary>
    void HandleDistanceControl()
    {
        // 滚轮调整距离
        float scroll = Input.GetAxis("Mouse ScrollWheel");
        if (Mathf.Abs(scroll) > 0.01f)
        {
            currentDistance -= scroll * scrollSensitivity;
            currentDistance = Mathf.Clamp(currentDistance, minDistance, maxDistance);
        }
        
        // 距离自动归位到基准值（与基准距离差异越大，速度越快）
        if (Mathf.Abs(currentDistance - baseDistance) > 0.1f)
        {
            float distanceDiff = Mathf.Abs(currentDistance - baseDistance);
            float normalizedDiff = distanceDiff / (maxDistance - baseDistance);
            float returnSpeed = Mathf.Lerp(distanceReturnSpeed, maxDistanceReturnSpeed, normalizedDiff);
            
            currentDistance = Mathf.MoveTowards(currentDistance, baseDistance, returnSpeed * Time.deltaTime);
        }
    }
    
    /// <summary>
    /// 处理Q/E键切换角度
    /// </summary>
    void HandleAngleSwitch()
    {
        // 检测按键持续按下状态
        bool qPressed = Input.GetKey(KeyCode.Q);
        bool ePressed = Input.GetKey(KeyCode.E);
        
        // 如果按住Q或E，且不在旋转中，立即开始旋转
        if (!isTransitioning)
        {
            if (qPressed)
            {
                RequestRotation(-90f); // Q键：顺时针旋转90°
            }
            else if (ePressed)
            {
                RequestRotation(90f); // E键：逆时针旋转90°
            }
        }
        // 如果正在旋转但按键首次按下，加入等待列表
        else
        {
            if (Input.GetKeyDown(KeyCode.Q))
            {
                RequestRotation(-90f);
            }
            else if (Input.GetKeyDown(KeyCode.E))
            {
                RequestRotation(90f);
            }
        }
        
        // 更新过渡状态
        if (isTransitioning)
        {
            transitionTimer += Time.deltaTime;
            if (transitionTimer >= rotationTransitionTime)
            {
                isTransitioning = false;
                currentYRotation = targetYRotation; // 过渡完成，更新当前角度
                lastRotationEndTime = Time.time; // 记录旋转结束时间
                Debug.Log($"[CamaraFollow] 旋转完成，当前角度: {currentYRotation:F1}°");
            }
        }
    }
    
    /// <summary>
    /// 请求旋转（处理等待列表逻辑）
    /// </summary>
    /// <param name="deltaAngle">角度增量（正数逆时针，负数顺时针）</param>
    void RequestRotation(float deltaAngle)
    {
        if (!isTransitioning)
        {
            // 不在旋转，直接开始
            StartTransition(deltaAngle);
            Debug.Log($"[CamaraFollow] 直接开始旋转: {deltaAngle:F0}°");
        }
        else
        {
            // 正在旋转，加入等待列表
            rotationWaitList.Enqueue(new RotationTask(deltaAngle, Time.time));
            Debug.Log($"[CamaraFollow] 旋转中，任务加入等待列表: {deltaAngle:F0}°, 队列长度: {rotationWaitList.Count}");
        }
    }
    
    /// <summary>
    /// 开始角度过渡
    /// </summary>
    /// <param name="deltaAngle">角度增量（正数顺时针，负数逆时针）</param>
    void StartTransition(float deltaAngle)
    {
        isTransitioning = true;
        transitionTimer = 0f;
        
        // 起始角度为当前角度
        startYRotation = currentYRotation;
        
        // 目标角度 = 当前角度 + 增量
        targetYRotation = currentYRotation + deltaAngle;
        
        Debug.Log($"[CamaraFollow] 旋转: {startYRotation:F1}° → {targetYRotation:F1}° (增量: {deltaAngle:F0}°)");
    }
    
    /// <summary>
    /// 初始化摄像头位置
    /// </summary>
    void InitializeCameraPosition()
    {
        // 火箭位置作为旋转中心
        Vector3 pivotPoint = target.position;
        
        // 计算初始位置（考虑基准角度偏移）
        float currentYAngle = currentYRotation + baseRotation.y;
        float angleInRadians = currentYAngle * Mathf.Deg2Rad;
        
        // 计算水平距离和高度（基于俯角baseRotation.x）
        float pitchRadians = baseRotation.x * Mathf.Deg2Rad;
        float horizontalDist = baseDistance * Mathf.Cos(pitchRadians);
        float height = baseDistance * Mathf.Sin(pitchRadians);
        
        Vector3 offset = new Vector3(
            Mathf.Sin(angleInRadians) * horizontalDist,
            height,
            -Mathf.Cos(angleInRadians) * horizontalDist
        );
        
        transform.position = pivotPoint + offset;
        
        // 第1步：面朝Rocket（LookAt确保视线对准）
        transform.LookAt(pivotPoint);
    }
    
    /// <summary>
    /// 更新摄像头位置和旋转
    /// </summary>
    void UpdateCameraPosition()
    {
        // 火箭位置作为旋转中心和LookAt目标
        Vector3 pivotPoint = target.position;
        
        // 计算当前Y角度
        float desiredYRotation;
        
        if (isTransitioning)
        {
            float t = transitionTimer / rotationTransitionTime;
            t = Mathf.SmoothStep(0f, 1f, t);
            desiredYRotation = Mathf.Lerp(startYRotation, targetYRotation, t);
        }
        else
        {
            desiredYRotation = currentYRotation;
        }
        
        // 应用基准Y角度偏移
        float currentYAngle = desiredYRotation + baseRotation.y;
        
        // 转换为弧度
        float angleInRadians = currentYAngle * Mathf.Deg2Rad;
        
        // 计算水平距离和高度（基于俯角baseRotation.x）
        float pitchRadians = baseRotation.x * Mathf.Deg2Rad;
        float horizontalDist = currentDistance * Mathf.Cos(pitchRadians);
        float height = currentDistance * Mathf.Sin(pitchRadians);
        
        // 第2步：计算摄像头位置（尽可能保持距离为baseDistance，当前距离为currentDistance）
        Vector3 offset = new Vector3(
            Mathf.Sin(angleInRadians) * horizontalDist,
            height,
            -Mathf.Cos(angleInRadians) * horizontalDist
        );
        
        // 设置位置
        transform.position = pivotPoint + offset;
        
        // 第1步：确保摄像头面向火箭（最高优先级）
        // LookAt会自动产生正确的俯角
        transform.LookAt(pivotPoint);
        
        // 每3秒输出调试信息
        debugTimer += Time.deltaTime;
        if (debugTimer >= 3f)
        {
            debugTimer = 0f;
            
            // Rocket坐标位置
            Vector3 rocketPos = target.position;
            
            // 摄像头视线方向向量
            Vector3 forward = transform.forward;
            
            // 判断主要面朝方向
            string direction = "";
            float absX = Mathf.Abs(forward.x);
            float absY = Mathf.Abs(forward.y);
            float absZ = Mathf.Abs(forward.z);
            
            if (absX > absY && absX > absZ)
            {
                direction = forward.x > 0 ? "X+" : "X-";
            }
            else if (absZ > absX && absZ > absY)
            {
                direction = forward.z > 0 ? "Z+" : "Z-";
            }
            else
            {
                direction = forward.y > 0 ? "Y+" : "Y-";
            }
            
            Vector3 currentRot = transform.eulerAngles;
            
            Debug.Log($"[CamaraFollow Debug] Rocket坐标: ({rocketPos.x:F2}, {rocketPos.y:F2}, {rocketPos.z:F2}) | " +
                     $"摄像头面朝方向: {direction} | " +
                     $"视线向量: (x={forward.x:F3}, y={forward.y:F3}, z={forward.z:F3}) | " +
                     $"摄像头角度: ({currentRot.x:F1}, {currentRot.y:F1}, {currentRot.z:F1}) | " +
                     $"距离: {currentDistance:F2}");
        }
    }
}
