using System;

[Serializable]
public class GridPathPointResult
{
    public float time_from_start;
    public float[] positions_rad;
    public float[] positions_deg;
    public float[] velocities;
    public float[] accelerations;
}

[Serializable]
public class GridPathResult
{
    public bool ok;
    public float fraction;
    public int grid_point_count;
    public int world_point_count;
    public int sampled_point_count;
    public int trajectory_point_count;
    public string[] joint_names;
    public GridPathPointResult[] points;
    public string error;
}