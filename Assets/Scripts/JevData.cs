using System.Collections.Generic;

[System.Serializable]
public class Probs
{
	public string label;
	public float probs;
}

[System.Serializable]
public class JevData
{
	public Probs[] positions;

	public Probs[] visibles;

	public Probs[] distances;
}