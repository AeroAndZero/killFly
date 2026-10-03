using System.Collections;
using UnityEngine;

public class UIController : MonoBehaviour
{
    public JevDataSO jevDataSO;
	Coroutine killAnimationCoroutine = null;
	public Color selectionCol;
	private Color noColor = new Color(0, 0, 0, 0);

	public void Start()
	{
		jevDataSO.killCount = 0;
	}

	public void updateScriptableObject(JevData jevData)
    {
		int i = 0;
		Color col;
		foreach (var position in jevData.positions)
		{
			if (i == 0) col = selectionCol;
			else col = noColor;
			switch (position.label)
			{
				case "top left":
					jevDataSO.topLeft = position.probs;
					jevDataSO.topLeftColor = col;
					break;
				case "top center":
					jevDataSO.topCenter = position.probs;
					jevDataSO.topCenterColor = col;
					break;
				case "top right":
					jevDataSO.topRight = position.probs;
					jevDataSO.topRightColor = col;
					break;
				case "center left":
					jevDataSO.centerLeft = position.probs;
					jevDataSO.centerLeftColor = col;
					break;
				case "center":
					jevDataSO.center = position.probs;
					jevDataSO.centerColor = col;
					break;
				case "center right":
					jevDataSO.centerRight = position.probs;
					jevDataSO.centerRightColor = col;
					break;
				case "bottom left":
					jevDataSO.bottomLeft = position.probs;
					jevDataSO.bottomLeftColor = col;
					break;
				case "bottom center":
					jevDataSO.bottomCenter = position.probs;
					jevDataSO.bottomCenterColor= col;
					break;
				case "bottom right":
					jevDataSO.bottomRight = position.probs;
					jevDataSO.bottomRightColor = col;
					break;
			}
			i++;
		}

		i = 0;
		foreach(var vis in jevData.visibles)
		{
			if (i == 0) col = selectionCol;
			else col = noColor;
			switch (vis.label)
			{
				case "yes":
					jevDataSO.yes = vis.probs;
					jevDataSO.yesColor = col;
					break;
				case "no":
					jevDataSO.no = vis.probs;
					jevDataSO.noColor = col;
					break;
			}
			i++;
		}

		i = 0;
		foreach(var dist in jevData.distances)
		{
			if (i == 0) col = selectionCol;
			else col = noColor;
			switch (dist.label)
			{
				case "very close":
					jevDataSO.veryClose = dist.probs;
					jevDataSO.veryCloseColor = col;
					break;
				case "close":
					jevDataSO.close = dist.probs;
					jevDataSO.closeColor = col;
					break;
				case "medium range":
					jevDataSO.mediumRange = dist.probs;
					jevDataSO.mediumRangeColor = col;
					break;
				case "far":
					jevDataSO.far = dist.probs;
					jevDataSO.farColor = col;
					break;
				case "very far":
					jevDataSO.veryFar = dist.probs;
					jevDataSO.veryFarColor = col;
					break;
			}
			i++;
		}
	}

	public void increaseKillCount()
	{
		jevDataSO.killCount += 1;
		killAnimation(0.8f,0.8f);
	}

	public void killAnimation(float timeToVisible, float timeToInvisible)
	{
		if (killAnimationCoroutine != null) StopCoroutine(killAnimationCoroutine);
		killAnimationCoroutine = StartCoroutine(killAnimationAlpha(timeToVisible, timeToInvisible));
	}

	IEnumerator killAnimationAlpha(float timeToVisible, float timeToInvisible)
	{
		float timeElapsed = 0f;
		jevDataSO.killAnimationAlpha = 0f;

		while (timeElapsed < timeToVisible)
		{
			float t = timeElapsed / timeToVisible;
			jevDataSO.killAnimationAlpha = Mathf.Lerp(0f, 1f, t);
			timeElapsed += Time.deltaTime;
			yield return null;
		}
		timeElapsed = 0f;
		jevDataSO.killAnimationAlpha = 1f;

		while (timeElapsed < timeToInvisible)
		{
			float t = timeElapsed / timeToInvisible;
			jevDataSO.killAnimationAlpha = Mathf.Lerp(1f, 0f, t);
			timeElapsed += Time.deltaTime;
			yield return null;
		}
		jevDataSO.killAnimationAlpha = 0f;
		killAnimationCoroutine = null;
	}
}
