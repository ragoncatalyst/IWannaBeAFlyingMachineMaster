using System.Collections;
using System.Collections.Generic;
using System.Linq;
using UnityEngine;
using UnityEngine.SceneManagement;
using TMPro;

public class Resulting : MonoBehaviour
{
    [Header("UI References")]
    [SerializeField] TextMeshProUGUI victoryText;         // 胜利文本
    [SerializeField] TextMeshProUGUI defeatText;          // 失败文本
    [SerializeField] TextMeshProUGUI defeatCommentText;   // 失败评语文本
    
    [Header("Victory/Defeat Settings")]
    [SerializeField] float levelLoadDelay = 3f;           // 场景加载延迟
    [SerializeField] float maxLandingSpeed = 5f;          // 最大安全着陆速度
    [SerializeField] float stoppedThreshold = 0.1f;       // 判定为停止的速度阈值
    [SerializeField] TextAsset failureCommentsFile;       // 失败评语文件
    
    private Rigidbody rb;
    private bool hasResult = false;                       // 是否已经有结果（胜利或失败）
    private bool isLanded = false;                        // 是否处于着陆状态
    private HashSet<Collision> activeCollisions = new HashSet<Collision>();  // 当前接触的碰撞
    private bool wasOverSpeed = false;                    // 上一帧是否超速
    private float lastFrameSpeed = 0f;                    // 上一帧的速度
    private bool hasEverHadVelocity = false;              // 是否曾经有过速度（非零）
    private Movement movementController;                  // 用于检查玩家是否操控过
    private float stoppedTime = 0f;                       // 速度为0持续的时间
    private bool hasDetectedStop = false;                 // 是否已经检测到速度变为0
    private const float stopConfirmationTime = 0.8f;      // 需要保持静止的时间（秒）
    private Dictionary<string, List<string>> failureComments = new Dictionary<string, List<string>>();  // 失败评语字典
    private List<string> lastContactedObjects = new List<string>();  // 最后接触的物体名称列表

    void Start()
    {
        rb = GetComponent<Rigidbody>();
        movementController = GetComponent<Movement>();
        
        // 初始化时隐藏所有文本
        if (victoryText != null)
        {
            victoryText.gameObject.SetActive(false);
        }
        if (defeatText != null)
        {
            defeatText.gameObject.SetActive(false);
        }
        if (defeatCommentText != null)
        {
            defeatCommentText.gameObject.SetActive(false);
        }
        
        lastFrameSpeed = 0f;
        hasEverHadVelocity = false;
        
        // 加载失败评语
        LoadFailureComments();
        
        Debug.Log("Resulting system initialized");
    }
    
    void LoadFailureComments()
    {
        if (failureCommentsFile == null)
        {
            Debug.LogError("Failure comments file not assigned!");
            return;
        }
        
        string[] lines = failureCommentsFile.text.Split(new[] { '\r', '\n' }, System.StringSplitOptions.RemoveEmptyEntries);
        string currentCategory = "";
        
        foreach (string line in lines)
        {
            string trimmedLine = line.Trim();
            
            // 跳过空行和注释行
            if (string.IsNullOrEmpty(trimmedLine) || trimmedLine.StartsWith("#"))
                continue;
            
            // 检查是否是新类别（格式: 00_1 "content"）
            if (trimmedLine.Contains(" \""))
            {
                int spaceIndex = trimmedLine.IndexOf(' ');
                string key = trimmedLine.Substring(0, spaceIndex);
                string category = key.Substring(0, 2);  // 提取前两位（如"00"、"01"等）
                
                // 如果是新类别，初始化列表
                if (category != currentCategory)
                {
                    if (!failureComments.ContainsKey(category))
                    {
                        failureComments[category] = new List<string>();
                    }
                    currentCategory = category;
                }
                
                // 提取引号内的内容
                int startQuote = trimmedLine.IndexOf('"');
                int endQuote = trimmedLine.LastIndexOf('"');
                if (startQuote != -1 && endQuote != -1 && endQuote > startQuote)
                {
                    string comment = trimmedLine.Substring(startQuote + 1, endQuote - startQuote - 1);
                    failureComments[currentCategory].Add(comment);
                }
            }
        }
        
        Debug.Log($"Loaded {failureComments.Count} failure comment categories");
    }

    void FixedUpdate()
    {
        // 如果已经有结果，不再检查
        if (hasResult) return;
        
        // 如果处于着陆状态，持续检查
        if (isLanded)
        {
            CheckLandingStatus();
        }
    }

    void OnCollisionEnter(Collision collision)
    {
        // 如果已经有结果，不再处理碰撞
        if (hasResult) return;
        
        // 检查碰撞速度
        float impactSpeed = collision.relativeVelocity.magnitude;
        
        Debug.Log($"Collision with {collision.gameObject.name} (Tag: {collision.gameObject.tag}), Impact speed: {impactSpeed:F2} m/s");
        
        // 先添加到接触列表
        activeCollisions.Add(collision);
        
        if (impactSpeed > maxLandingSpeed)
        {
            // 超速碰撞，需要判定失败类型
            Debug.Log($"<color=red>FAILED: Impact speed too high! {impactSpeed:F1} m/s > {maxLandingSpeed} m/s</color>");
            
            // 判定超速撞击的类型
            string name = collision.gameObject.name;
            string tag = collision.gameObject.tag;
            
            string failureCategory = "00";
            if (name == "LaunchingPad")
            {
                failureCategory = "03";
            }
            else if (name == "LandingPad")
            {
                failureCategory = "04";
            }
            else if (tag == "Terrain")
            {
                failureCategory = "05";
            }
            
            TriggerDefeat(failureCategory);
            return;
        }
        
        // 没有超速，进入着陆状态
        if (!isLanded)
        {
            isLanded = true;
            Debug.Log("<color=yellow>Entered landing state - started monitoring</color>");
        }
    }

    void OnCollisionStay(Collision collision)
    {
        // 确保碰撞在列表中
        if (!hasResult && !activeCollisions.Contains(collision))
        {
            activeCollisions.Add(collision);
        }
        
        // 持续更新最后接触的物体列表
        if (!hasResult)
        {
            string name = collision.gameObject.name;
            if (!lastContactedObjects.Contains(name))
            {
                lastContactedObjects.Add(name);
                Debug.Log($"Added to lastContactedObjects: {name}");
            }
        }
    }

    void OnCollisionExit(Collision collision)
    {
        // 从接触列表中移除
        activeCollisions.Remove(collision);
        
        Debug.Log($"Left contact with {collision.gameObject.name}, remaining contacts: {activeCollisions.Count}");
        
        // 如果没有任何接触了，离开着陆状态
        if (activeCollisions.Count == 0)
        {
            isLanded = false;
            hasDetectedStop = false;  // 重置停止检测
            stoppedTime = 0f;          // 重置停止计时
            lastContactedObjects.Clear();  // 清空最后接触的物体列表
            Debug.Log("<color=cyan>Left landing state - took off again</color>");
        }
    }

    void CheckLandingStatus()
    {
        // 检查是否还有接触物体
        if (activeCollisions.Count == 0)
        {
            isLanded = false;
            lastFrameSpeed = rb.velocity.magnitude;
            hasDetectedStop = false;
            stoppedTime = 0f;
            return;
        }
        
        // 检查当前速度
        float currentSpeed = rb.velocity.magnitude;
        
        // 只有在玩家操控过火箭后，才开始追踪速度变化
        if (movementController != null && movementController.HasPlayerControlled())
        {
            // 记录是否曾经有过速度
            if (currentSpeed > stoppedThreshold)
            {
                hasEverHadVelocity = true;
            }
            
            // 检查是否从有速度降到无速度
            bool isStopped = currentSpeed <= stoppedThreshold;
            bool wasMoving = lastFrameSpeed > stoppedThreshold;
            
            // 首次检测到速度变为0
            if (isStopped && wasMoving && hasEverHadVelocity && !hasDetectedStop)
            {
                hasDetectedStop = true;
                stoppedTime = 0f;
                Debug.Log($"<color=yellow>Rocket speed dropped to 0! Now monitoring for {stopConfirmationTime}s to confirm stable stop...</color>");
            }
            
            // 如果已经检测到速度为0，继续计时
            if (hasDetectedStop && isStopped)
            {
                stoppedTime += Time.fixedDeltaTime;
                
                // 如果速度在0.8秒内一直保持为0，判定为稳定停止
                if (stoppedTime >= stopConfirmationTime)
                {
                    Debug.Log($"<color=yellow>Rocket confirmed stable! Stopped for {stoppedTime:F2}s, checking victory conditions...</color>");
                    hasDetectedStop = false;  // 重置，防止重复判定
                    stoppedTime = 0f;
                    CheckVictoryConditions();
                }
            }
            else if (hasDetectedStop && !isStopped)
            {
                // 速度不为0了，重置计时
                Debug.Log($"<color=cyan>Rocket started moving again during confirmation period! Speed: {currentSpeed:F2} m/s</color>");
                hasDetectedStop = false;
                stoppedTime = 0f;
            }
        }
        
        // 检测速度变化（避免每帧输出）
        bool isOverSpeed = currentSpeed > maxLandingSpeed;
        if (isOverSpeed != wasOverSpeed)
        {
            if (isOverSpeed)
            {
                Debug.Log($"<color=orange>WARNING: Speed increased to {currentSpeed:F2} m/s (above safe limit {maxLandingSpeed} m/s)</color>");
            }
            else
            {
                Debug.Log($"<color=green>Speed decreased to safe range: {currentSpeed:F2} m/s</color>");
            }
            wasOverSpeed = isOverSpeed;
        }
        
        // 更新上一帧的速度
        lastFrameSpeed = currentSpeed;
    }

    void CheckVictoryConditions()
    {
        // 使用最后接触的物体列表而不是activeCollisions
        List<string> landingPads = new List<string>();
        List<string> launchingPads = new List<string>();
        List<string> otherObjects = new List<string>();
        bool landingPadHasFinish = false;
        
        Debug.Log($"CheckVictoryConditions - lastContactedObjects: {string.Join(", ", lastContactedObjects)}");
        
        foreach (string objectName in lastContactedObjects)
        {
            if (objectName == "LandingPad")
            {
                landingPads.Add(objectName);
                // 检查LandingPad是否有Finish标签（需要在场景中查找）
                GameObject obj = GameObject.Find(objectName);
                if (obj != null && obj.CompareTag("Finish"))
                {
                    landingPadHasFinish = true;
                }
            }
            else if (objectName == "LaunchingPad")
            {
                launchingPads.Add(objectName);
            }
            else
            {
                otherObjects.Add(objectName);
            }
        }
        
        Debug.Log($"CheckVictoryConditions - LandingPads={landingPads.Count}(Finish={landingPadHasFinish}), LaunchingPads={launchingPads.Count}, Others={otherObjects.Count}");
        
        // 判定胜利：必须有LandingPad且有Finish标签，且没有LaunchingPad和其他物体
        if (landingPads.Count > 0 && landingPadHasFinish && launchingPads.Count == 0 && otherObjects.Count == 0)
        {
            Debug.Log("<color=green>VICTORY! Landed safely on LandingPad!</color>");
            TriggerVictory();
        }
        else
        {
            // 失败判定
            DeterminFailureType(launchingPads.Count > 0, landingPads.Count > 0, otherObjects.Count > 0, lastFrameSpeed > maxLandingSpeed);
        }
    }
    
    void DeterminFailureType(bool hasLaunchingPad, bool hasLandingPad, bool hasOtherObjects, bool wasExcessiveSpeed)
    {
        string failureCategory = "00";
        
        Debug.Log($"DeterminFailureType - LaunchingPad={hasLaunchingPad}, LandingPad={hasLandingPad}, Others={hasOtherObjects}, ExcessiveSpeed={wasExcessiveSpeed}");
        
        if (wasExcessiveSpeed)
        {
            // 超速情况：03, 04, 05
            if (hasLaunchingPad)
            {
                failureCategory = "03";
                Debug.Log("Failure Type: 03 - Excessive speed on LaunchingPad");
            }
            else if (hasLandingPad)
            {
                failureCategory = "04";
                Debug.Log("Failure Type: 04 - Excessive speed on LandingPad");
            }
            else if (hasOtherObjects)
            {
                failureCategory = "05";
                Debug.Log("Failure Type: 05 - Excessive speed on other objects");
            }
        }
        else
        {
            // 无超速情况：01, 02, 06
            if (hasLaunchingPad && !hasLandingPad && !hasOtherObjects)
            {
                failureCategory = "01";
                Debug.Log("Failure Type: 01 - Only LaunchingPad");
            }
            else if (hasLaunchingPad && hasOtherObjects && !hasLandingPad)
            {
                failureCategory = "02";
                Debug.Log("Failure Type: 02 - LaunchingPad with others");
            }
            else if (hasLandingPad && hasOtherObjects && !hasLaunchingPad)
            {
                failureCategory = "06";
                Debug.Log("Failure Type: 06 - LandingPad with others");
            }
            else
            {
                failureCategory = "00";
                Debug.Log("Failure Type: 00 - Other case");
            }
        }
        
        TriggerDefeat(failureCategory);
    }

    void TriggerVictory()
    {
        hasResult = true;
        Debug.Log("Victory! Level Complete!");
        
        // 显示胜利文本
        if (victoryText != null)
        {
            victoryText.gameObject.SetActive(true);
        }
        
        // 停止音效
        AudioSource audioSource = GetComponent<AudioSource>();
        if (audioSource != null)
        {
            audioSource.Stop();
        }
        
        // 延迟加载下一关
        Invoke("LoadNextLevel", levelLoadDelay);
    }

    void TriggerDefeat(string failureCategory)
    {
        hasResult = true;
        Debug.Log($"Defeat - Category: {failureCategory}");
        
        // 显示失败文本
        if (defeatText != null)
        {
            defeatText.gameObject.SetActive(true);
        }
        
        // 显示失败评语
        if (defeatCommentText != null)
        {
            string comment = GetRandomFailureComment(failureCategory);
            defeatCommentText.text = comment;
            defeatCommentText.gameObject.SetActive(true);
            Debug.Log($"Failure comment: {comment}");
        }
        
        // 停止音效
        AudioSource audioSource = GetComponent<AudioSource>();
        if (audioSource != null)
        {
            audioSource.Stop();
        }
        
        // 延迟重新加载当前场景
        Invoke("ReloadLevel", levelLoadDelay);
    }
    
    string GetRandomFailureComment(string category)
    {
        // 如果找不到该类别，使用默认类别00
        if (!failureComments.ContainsKey(category))
        {
            category = "00";
        }
        
        List<string> comments = failureComments[category];
        if (comments == null || comments.Count == 0)
        {
            return "Mission Failed.";
        }
        
        // 随机选择一条评语
        int randomIndex = Random.Range(0, comments.Count);
        return comments[randomIndex];
    }

    void LoadNextLevel()
    {
        int currentSceneIndex = SceneManager.GetActiveScene().buildIndex;
        int nextSceneIndex = currentSceneIndex + 1;
        
        // 如果是最后一关，回到第一关
        if (nextSceneIndex >= SceneManager.sceneCountInBuildSettings)
        {
            nextSceneIndex = 0;
        }
        
        SceneManager.LoadScene(nextSceneIndex);
    }

    void ReloadLevel()
    {
        int currentSceneIndex = SceneManager.GetActiveScene().buildIndex;
        SceneManager.LoadScene(currentSceneIndex);
    }
}
