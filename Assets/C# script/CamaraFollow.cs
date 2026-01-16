using System.Collections;
using System.Collections.Generic;
using UnityEngine;

public class CamaraFollow : MonoBehaviour
{
    [Header("目标设置")]
    [SerializeField] Transform target; // 要跟随的目标（火箭）
    
    [Header("摄像头距离设置")]
    [SerializeField] float baseDistance = 12f;    // 基础距离
    [SerializeField] float minDistance = 5f;      // 最小距离
    [SerializeField] float maxDistance = 25f;     // 最大距离
    [SerializeField] float scrollSensitivity = 2f; // 滚轮灵敏度
    [SerializeField] float heightOffset = 4f;    // 高度偏移
    
    [Header("鼠标旋转设置")]
    [SerializeField] float mouseSensitivity = 450f;  // 鼠标灵敏度（提升旋转效率）
    [SerializeField] float minVerticalAngle = -20f;   // 最小俯仰角
    [SerializeField] float maxVerticalAngle = 80f;    // 最大俯仰角
    
    [Header("障碍物透明设置")]
    [SerializeField] float obstacleTransparency = 0.5f;  // 障碍物透明度
    [SerializeField] float transparencySpeed = 3f;       // 透明度变化速度
    [SerializeField] LayerMask obstacleLayer = ~0;       // 障碍物图层
    
    private Rigidbody targetRb;
    private float horizontalAngle = 0f;   // 水平旋转角度
    private float verticalAngle = 20f;    // 垂直旋转角度
    private float currentDistance;        // 当前距离
    private Dictionary<Renderer, Material[]> originalMaterials = new Dictionary<Renderer, Material[]>();
    private Dictionary<Renderer, Material[]> transparentMaterials = new Dictionary<Renderer, Material[]>();
    private HashSet<Renderer> currentObstacles = new HashSet<Renderer>();

    void Start()
    {
        if (target == null)
        {
            target = FindObjectOfType<Movement>().transform;
        }
    
        if (target != null)
        {
            targetRb = target.GetComponent<Rigidbody>();
            currentDistance = baseDistance;
            
            // 初始化摄像头角度为火箭正前方
            Vector3 direction = transform.position - target.position;
            horizontalAngle = Mathf.Atan2(direction.x, direction.z) * Mathf.Rad2Deg + 180f;
            verticalAngle = Mathf.Asin(direction.y / direction.magnitude) * Mathf.Rad2Deg;
        }
        
        // 锁定并隐藏鼠标光标（像Minecraft一样）
        Cursor.lockState = CursorLockMode.Locked;
        Cursor.visible = false;
    }

    void LateUpdate()
    {
        if (target == null) return;
        
        HandleMouseRotation();
        UpdateCameraPosition();
        HandleObstacles();
    }
    
    void HandleMouseRotation()
    {
        // 只有当鼠标移动时才旋转摄像头
        float mouseX = Input.GetAxis("Mouse X");
        float mouseY = Input.GetAxis("Mouse Y");
        
        if (Mathf.Abs(mouseX) > 0.01f || Mathf.Abs(mouseY) > 0.01f)
        {
            horizontalAngle += mouseX * mouseSensitivity * Time.deltaTime;
            verticalAngle -= mouseY * mouseSensitivity * Time.deltaTime;
            verticalAngle = Mathf.Clamp(verticalAngle, minVerticalAngle, maxVerticalAngle);
        }
        
        // 滚轮控制距离
        float scroll = Input.GetAxis("Mouse ScrollWheel");
        if (Mathf.Abs(scroll) > 0.01f)
        {
            currentDistance -= scroll * scrollSensitivity;
            currentDistance = Mathf.Clamp(currentDistance, minDistance, maxDistance);
        }
    }
    
    void UpdateCameraPosition()
    {
        // 根据角度计算摄像头位置
        Quaternion rotation = Quaternion.Euler(verticalAngle, horizontalAngle, 0);
        Vector3 offset = rotation * new Vector3(0, heightOffset, -currentDistance);
        
        // 直接设置位置，不使用平滑插值
        transform.position = target.position + offset;
        
        // 直接朝向目标，不使用平滑插值
        transform.LookAt(target.position);
    }
    
    void HandleObstacles()
    {
        HashSet<Renderer> newObstacles = new HashSet<Renderer>();
        
        // 从摄像头向目标发射射线
        Vector3 direction = target.position - transform.position;
        float distance = direction.magnitude;
        
        RaycastHit[] hits = Physics.RaycastAll(transform.position, direction.normalized, distance, obstacleLayer);
        
        foreach (RaycastHit hit in hits)
        {
            // 忽略目标本身
            if (hit.transform == target || hit.transform.IsChildOf(target))
                continue;
                
            Renderer renderer = hit.collider.GetComponent<Renderer>();
            if (renderer != null)
            {
                newObstacles.Add(renderer);
                
                // 如果是新障碍物，保存原始材质
                if (!originalMaterials.ContainsKey(renderer))
                {
                    originalMaterials[renderer] = renderer.materials;
                    
                    // 创建透明材质副本
                    Material[] transparentMats = new Material[renderer.materials.Length];
                    for (int i = 0; i < renderer.materials.Length; i++)
                    {
                        transparentMats[i] = new Material(renderer.materials[i]);
                        SetMaterialTransparent(transparentMats[i]);
                    }
                    transparentMaterials[renderer] = transparentMats;
                }
                
                // 应用透明材质
                renderer.materials = transparentMaterials[renderer];
                
                // 逐渐降低透明度
                foreach (Material mat in renderer.materials)
                {
                    Color color = mat.color;
                    color.a = Mathf.Lerp(color.a, obstacleTransparency, Time.deltaTime * transparencySpeed);
                    mat.color = color;
                }
            }
        }
        
        // 恢复不再被遮挡的物体
        List<Renderer> toRemove = new List<Renderer>();
        foreach (Renderer renderer in currentObstacles)
        {
            if (!newObstacles.Contains(renderer))
            {
                // 逐渐恢复不透明度
                bool fullyRestored = true;
                foreach (Material mat in renderer.materials)
                {
                    Color color = mat.color;
                    color.a = Mathf.Lerp(color.a, 1f, Time.deltaTime * transparencySpeed);
                    mat.color = color;
                    
                    if (color.a < 0.99f)
                        fullyRestored = false;
                }
                
                // 完全恢复后，使用原始材质
                if (fullyRestored && originalMaterials.ContainsKey(renderer))
                {
                    renderer.materials = originalMaterials[renderer];
                    toRemove.Add(renderer);
                }
            }
        }
        
        // 清理已恢复的物体
        foreach (Renderer renderer in toRemove)
        {
            currentObstacles.Remove(renderer);
            if (transparentMaterials.ContainsKey(renderer))
            {
                foreach (Material mat in transparentMaterials[renderer])
                {
                    Destroy(mat);
                }
                transparentMaterials.Remove(renderer);
            }
            originalMaterials.Remove(renderer);
        }
        
        currentObstacles = newObstacles;
    }
    
    void SetMaterialTransparent(Material mat)
    {
        // 设置材质为透明模式
        mat.SetFloat("_Mode", 3); // Transparent mode
        mat.SetInt("_SrcBlend", (int)UnityEngine.Rendering.BlendMode.SrcAlpha);
        mat.SetInt("_DstBlend", (int)UnityEngine.Rendering.BlendMode.OneMinusSrcAlpha);
        mat.SetInt("_ZWrite", 0);
        mat.DisableKeyword("_ALPHATEST_ON");
        mat.EnableKeyword("_ALPHABLEND_ON");
        mat.DisableKeyword("_ALPHAPREMULTIPLY_ON");
        mat.renderQueue = 3000;
    }
    
    void OnDestroy()
    {
        // 清理所有透明材质
        foreach (var pair in transparentMaterials)
        {
            foreach (Material mat in pair.Value)
            {
                if (mat != null)
                    Destroy(mat);
            }
        }
        
        // 恢复所有原始材质
        foreach (var pair in originalMaterials)
        {
            if (pair.Key != null)
                pair.Key.materials = pair.Value;
        }
    }
}
