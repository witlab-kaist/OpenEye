using UnityEngine;
using System.IO;

public class NeonExperimentManager : MonoBehaviour
{
    [Header("References")]
    public TargetSpawner spawner;

    [Header("Trial Params")]
    public float[] radiusConditionsMeters = { 0.20f, 0.30f };
    public float[] widthConditionsMeters = { 0.05f, 0.08f };
    public int numTargets = 8;
    public int repsPerBlock = 10;
    [Header("Trial Params")]
    public string csvFileHeader = "neonxr";

    private int currBlockIdx = 0;
    private int currRep = 0;

    private int startIdx = 0;
    private int endIdx = 0;

    private float trialStartTime;

    // --- logging state ---
    private float lastSelTime = 0f;
    private Vector2 lastSelPos = Vector2.zero;

    private float currSelTime = 0f;
    private Vector2 currSelPos = Vector2.zero;

    private StreamWriter summaryWriter;

    void Start()
    {
        string desktopPath = System.Environment.GetFolderPath(System.Environment.SpecialFolder.Desktop);
        string saveDir = Path.Combine(desktopPath, "quest_fitts");

        if (!Directory.Exists(saveDir))
            Directory.CreateDirectory(saveDir);

        string fileName = csvFileHeader + "FittsSummary_" + System.DateTime.Now.ToString("yyyyMMdd_HHmmss") + ".csv";
        string path = Path.Combine(saveDir, fileName);

        summaryWriter = new StreamWriter(path);
        summaryWriter.WriteLine(
            "block,rep,dist,width,currSelTime,lastSelTime,selX,selY,targetX,targetY,duration,distanceTravelled,distFromTargetX,distFromTargetY,distFromTarget,success"
        );

        SetupBlock(currBlockIdx);
        BeginNextRep();

        lastSelTime = Time.time;
        lastSelPos = Vector2.zero;
    }

    void OnDestroy()
    {
        summaryWriter?.Flush();
        summaryWriter?.Close();
    }

    void SetupBlock(int blockIdx)
    {
        int rCount = radiusConditionsMeters.Length;
        int wCount = widthConditionsMeters.Length;

        int rIdx = blockIdx / wCount;
        int wIdx = blockIdx % wCount;

        rIdx = Mathf.Clamp(rIdx, 0, rCount - 1);
        wIdx = Mathf.Clamp(wIdx, 0, wCount - 1);

        float radius = radiusConditionsMeters[rIdx];
        float diam = widthConditionsMeters[wIdx];

        spawner.radiusMeters = radius;
        spawner.targetDiameterMeters = diam;
        spawner.numTargets = numTargets;

        spawner.SpawnTargets();
    }

    void BeginNextRep()
    {
        int pairsPerRing = numTargets / 2;
        int repsPerRound = pairsPerRing * 2;

        int roundIndex = currRep / repsPerRound;
        int repInRound = currRep % repsPerRound;

        int pairIndexInRound = repInRound / 2;

        int a = pairIndexInRound;                    // low index in that pair
        int b = pairIndexInRound + pairsPerRing;     // high index in that pair

        bool roundStartsLowToHigh = (roundIndex % 2 == 0); // even rounds: a→b first
        bool firstOfPair = (repInRound % 2 == 0);          // first half or second half of the pair

        if (roundStartsLowToHigh)
        {
            if (firstOfPair)
            {
                startIdx = a;
                endIdx = b;
            }
            else
            {
                startIdx = b;
                endIdx = a;
            }
        }
        else
        {
            if (firstOfPair)
            {
                startIdx = b;
                endIdx = a;
            }
            else
            {
                startIdx = a;
                endIdx = b;
            }
        }

        HighlightTargets();
        trialStartTime = Time.time;
    }

    void HighlightTargets()
    {
        for (int i = 0; i < spawner.spawnedTargets.Count; i++)
        {
            var tb = spawner.spawnedTargets[i].GetComponent<TargetBehavior>();
            if (tb != null)
            {
                tb.SetNeutral();
            }
        }

        if (startIdx >= 0 && startIdx < spawner.spawnedTargets.Count)
        {
            var tbStart = spawner.spawnedTargets[startIdx].GetComponent<TargetBehavior>();
            if (tbStart != null)
            {
                tbStart.SetStart();
            }
        }

        if (endIdx >= 0 && endIdx < spawner.spawnedTargets.Count)
        {
            var tbEnd = spawner.spawnedTargets[endIdx].GetComponent<TargetBehavior>();
            if (tbEnd != null)
            {
                tbEnd.SetEnd();
            }
        }
    }

    public void OnTargetSelected(
        int hitIndex,
        Vector2 hitPos2D,
        Vector3 planeCenter,
        Vector3 rightFlat,
        Vector3 upWorld
    )
    {
        currSelTime = Time.time;
        currSelPos = hitPos2D;

        Vector2 targetPos2D = Vector2.zero;
        if (endIdx >= 0 && endIdx < spawner.spawnedTargets.Count)
        {
            Vector3 tgtWorld = spawner.spawnedTargets[endIdx].transform.position;

            Vector3 toTarget = tgtWorld - planeCenter;
            float tx = Vector3.Dot(toTarget, rightFlat); // plane X
            float ty = Vector3.Dot(toTarget, upWorld);   // plane Y

            targetPos2D = new Vector2(tx, ty);
        }

        float duration = currSelTime - lastSelTime;
        float distanceTravelled = Vector2.Distance(lastSelPos, currSelPos);

        Vector2 distFromTarget = targetPos2D - currSelPos;
        float distFromTargetXY = Vector2.Distance(targetPos2D, currSelPos);

        int success = (hitIndex == endIdx) ? 1 : 0;

        if (summaryWriter != null)
        {
            string line =
                currBlockIdx + "," +                     // block
                currRep + "," +                          // rep
                spawner.radiusMeters + "," +             // dist (ring radius)
                spawner.targetDiameterMeters + "," +     // width (target diameter)
                currSelTime + "," +                      // currSelTime
                lastSelTime + "," +                      // lastSelTime
                currSelPos.x + "," +                     // selX
                currSelPos.y + "," +                     // selY
                targetPos2D.x + "," +                    // targetX
                targetPos2D.y + "," +                    // targetY
                duration + "," +                         // duration
                distanceTravelled + "," +                // distanceTravelled
                distFromTarget.x + "," +                 // distFromTargetX
                distFromTarget.y + "," +                 // distFromTargetY
                distFromTargetXY + "," +                 // distFromTargetXY
                success;

            summaryWriter.WriteLine(line);
            summaryWriter.Flush();
        }

        lastSelTime = currSelTime;
        lastSelPos = currSelPos;

        currRep++;

        if (currRep >= repsPerBlock)
        {
            currRep = 0;
            currBlockIdx++;

            int totalBlocks = radiusConditionsMeters.Length * widthConditionsMeters.Length;
            if (currBlockIdx >= totalBlocks)
            {
                Debug.Log(">>>> Expermient Complete!");
                return;
            }

            SetupBlock(currBlockIdx);
        }

        BeginNextRep();
    }
    
}
