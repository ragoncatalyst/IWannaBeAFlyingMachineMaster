using System.Collections;
using System.Collections.Generic;
using UnityEngine;
using UnityEngine.UI;
using TMPro;

public class Navigator : MonoBehaviour
{
    [SerializeField] Transform rocketTransform;           // 火箭物体
    [SerializeField] Transform radarSphereTransform;      // 雷达球体（Rocket的子物体）
    [SerializeField] Transform landingPadTransform;       // 登陆台物体
    [SerializeField] TextMeshProUGUI distanceText;        // 距离显示文本
    [SerializeField] Canvas canvas;                      // Canvas（用于转换坐标）
    [SerializeField] float radarDistance = 5f;            // RadarSphere与Rocket的距离
    [SerializeField] float blinkCycle = 2f;               // 闪烁周期（秒）
    [SerializeField] float blinkOnDuration = 1f;          // 亮起持续时间（秒）
    [SerializeField] float minScale = 0.2f;               // 最小缩放值
    [SerializeField] float maxScale = 1.5f;               // 最大缩放值
    [SerializeField] float scaleDistance = 100f;          // 缩放参考距离（超过此距离缩放到最小）
    
    private Vector3 initialLocalPosition;                 // RadarSphere的初始本地位置
    private Vector3 initialScale;                         // RadarSphere的初始缩放
    private Renderer radarRenderer;                       // RadarSphere的渲染器
    private Camera mainCamera;                            // 主摄像机

    void Start()
    {
        // 如果没有手动分配，尝试自动查找
        if (rocketTransform == null)
        {
            rocketTransform = FindObjectOfType<Movement>()?.GetComponent<Transform>();
        }
        
        if (landingPadTransform == null)
        {
            landingPadTransform = GameObject.FindWithTag("Finish")?.transform;
        }

        if (radarSphereTransform == null)
        {
            // 尝试在Rocket的子物体中查找名为RadarSphere的物体
            if (rocketTransform != null)
            {
                Transform found = rocketTransform.Find("RadarSphere");
                if (found != null)
                {
                    radarSphereTransform = found;
                }
            }
        }

        // 记录RadarSphere的初始本地位置
        if (radarSphereTransform != null)
        {
            initialLocalPosition = radarSphereTransform.localPosition;
            initialScale = radarSphereTransform.localScale;
            radarRenderer = radarSphereTransform.GetComponent<Renderer>();
        }

        // 获取主摄像机
        mainCamera = Camera.main;

        // 如果没有手动分配Canvas，尝试自动查找
        if (canvas == null)
        {
            canvas = FindObjectOfType<Canvas>();
        }

        // 如果没有手动分配TextMeshProUGUI，尝试自动查找
        if (distanceText == null)
        {
            distanceText = FindObjectOfType<TextMeshProUGUI>();
        }
    }

    void Update()
    {
        UpdateRadar();
        UpdateBlink();
        UpdateDistanceDisplay();
    }

    void UpdateRadar()
    {
        // 检查必要的组件是否存在
        if (rocketTransform == null || radarSphereTransform == null || landingPadTransform == null)
        {
            return;
        }

        // 检查LandingPad是否在相机视野内
        if (mainCamera != null && IsTargetVisible())
        {
            // 如果LandingPad在视野内，隐藏radar
            if (radarRenderer != null)
            {
                radarRenderer.enabled = false;
            }
            return;
        }
        else
        {
            // 如果LandingPad不在视野内，显示radar
            if (radarRenderer != null)
            {
                radarRenderer.enabled = true;
            }
        }

        // 计算从Rocket到LandingPad的方向
        Vector3 toTargetVector = landingPadTransform.position - rocketTransform.position;
        
        // 计算XZ平面的距离（水平距离）
        Vector3 toTargetVectorXZ = new Vector3(toTargetVector.x, 0, toTargetVector.z);
        float distanceXZ = toTargetVectorXZ.magnitude;
        
        toTargetVector.Normalize();

        // 保持距离不变，将RadarSphere放在指向目标的方向上
        Vector3 radarNewPosition = rocketTransform.position + toTargetVector * radarDistance;
        radarSphereTransform.position = radarNewPosition;

        // 让RadarSphere指向LandingPad（旋转使其forward指向目标）
        radarSphereTransform.LookAt(landingPadTransform);
        
        // 根据XZ距离缩放RadarSphere - 距离越远，球越小
        float scaleRatio = Mathf.Clamp01(1.0f - (distanceXZ / scaleDistance));
        float targetScale = Mathf.Lerp(minScale, maxScale, scaleRatio);
        radarSphereTransform.localScale = initialScale * targetScale;
    }

    // 检查LandingPad是否在相机视野内
    bool IsTargetVisible()
    {
        if (mainCamera == null || landingPadTransform == null)
            return false;

        // 获取LandingPad的Renderer来检测可见性
        Renderer targetRenderer = landingPadTransform.GetComponent<Renderer>();
        if (targetRenderer != null)
        {
            // 使用GeometryUtility检查物体是否在视锥体内
            Plane[] planes = GeometryUtility.CalculateFrustumPlanes(mainCamera);
            return GeometryUtility.TestPlanesAABB(planes, targetRenderer.bounds);
        }

        // 如果没有Renderer，使用视口坐标判断
        Vector3 viewportPoint = mainCamera.WorldToViewportPoint(landingPadTransform.position);
        
        // 检查是否在视口内（x和y在0-1之间，z>0表示在相机前方）
        return viewportPoint.z > 0 && 
               viewportPoint.x > 0 && viewportPoint.x < 1 && 
               viewportPoint.y > 0 && viewportPoint.y < 1;
    }

    void UpdateBlink()
    {
        // 检查radarRenderer是否存在
        if (radarRenderer == null)
        {
            return;
        }

        // 计算当前在闪烁周期中的位置（0到blinkCycle之间循环）
        float timeInCycle = Time.time % blinkCycle;

        // 如果在闪烁周期的前blinkOnDuration秒内，则显示；否则隐藏
        bool shouldBlink = timeInCycle < blinkOnDuration;
        radarRenderer.enabled = shouldBlink;

        // 同步TMP的显示/隐藏
        if (distanceText != null)
        {
            distanceText.enabled = shouldBlink;
        }
    }

    void UpdateDistanceDisplay()
    {
        // 检查必要的组件
        if (radarSphereTransform == null || distanceText == null || mainCamera == null || canvas == null)
        {
            return;
        }

        // 计算距离
        if (landingPadTransform != null && rocketTransform != null)
        {
            Vector3 toTargetVector = landingPadTransform.position - rocketTransform.position;
            float distanceToTarget = toTargetVector.magnitude;
            distanceText.text = $"{distanceToTarget:F1}m";
        }

        // 将RadarSphere的世界坐标转换为屏幕坐标
        Vector3 screenPos = mainCamera.WorldToScreenPoint(radarSphereTransform.position);

        // 检查RadarSphere是否在摄像机前方
        if (screenPos.z > 0)
        {
            // 将屏幕坐标转换为Canvas坐标
            RectTransformUtility.ScreenPointToLocalPointInRectangle(
                canvas.GetComponent<RectTransform>(),
                screenPos,
                canvas.worldCamera,
                out Vector2 canvasPos
            );

            // 更新TMP的位置
            RectTransform textRectTransform = distanceText.GetComponent<RectTransform>();
            if (textRectTransform != null)
            {
                textRectTransform.anchoredPosition = canvasPos;
            }
        }
    }
}
