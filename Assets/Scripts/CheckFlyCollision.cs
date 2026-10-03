using NUnit.Framework;
using System;
using UnityEditor;
using UnityEngine;
using UnityEngine.Events;

public class CheckFlyCollision : MonoBehaviour
{
	bool isTouchingFly = false;
	public UIController uiController;
	public FlyMovement flyMovement;

	private void OnTriggerEnter(Collider collider)
	{
		if(collider.tag == "Fly")
		{
			isTouchingFly = true;
		}
	}

	private void OnTriggerExit(Collider collider)
	{
		if(collider.tag == "Fly")
		{
			isTouchingFly = false;
		}
	}

	public void AnimeTrigger()
	{
		// Debug.LogError("Animation event check triggered");
		if (isTouchingFly)
		{
			Debug.Log("----- FLY HAS BEEN KILLED -------");
			uiController.increaseKillCount();
			flyMovement.RestFly();
		}
	}
}
