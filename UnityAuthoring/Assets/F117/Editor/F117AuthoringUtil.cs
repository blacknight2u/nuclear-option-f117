using System;
using System.Linq;
using UnityEditor;
using UnityEngine;

internal static class F117AuthoringUtil
{
    internal static Transform FindDeep(Transform root, string name)
    {
        if (root.name == name)
            return root;
        foreach (Transform child in root)
        {
            Transform found = FindDeep(child, name);
            if (found != null)
                return found;
        }
        return null;
    }

    internal static Component FindComponent(GameObject root, string typeName)
    {
        return root.GetComponentsInChildren<Component>(true)
            .FirstOrDefault(component => component != null && component.GetType().Name == typeName);
    }

    internal static Component[] FindComponents(GameObject root, string typeName)
    {
        return root.GetComponentsInChildren<Component>(true)
            .Where(component => component != null && component.GetType().Name == typeName)
            .ToArray();
    }

    internal static Type FindType(string typeName)
    {
        Type type = AppDomain.CurrentDomain.GetAssemblies()
            .Select(assembly => assembly.GetType(typeName, false))
            .FirstOrDefault(candidate => candidate != null);
        if (type == null)
            throw new InvalidOperationException("The authoring assembly does not contain " + typeName + ".");
        return type;
    }

    internal static Type FindRuntimeType(string typeName)
    {
        Type type = FindType(typeName);
        if (!typeof(Component).IsAssignableFrom(type))
            throw new InvalidOperationException(typeName + " is not a Unity component type.");
        return type;
    }

    internal static Component AddRuntimeComponent(GameObject target, string typeName)
    {
        return target.AddComponent(FindRuntimeType(typeName));
    }

    internal static SerializedProperty Require(SerializedObject serialized, string name)
    {
        SerializedProperty property = serialized.FindProperty(name);
        if (property == null)
            throw new InvalidOperationException(serialized.targetObject.name + " has no serialized field '" + name + "'.");
        return property;
    }

    internal static SerializedProperty Require(SerializedProperty serialized, string name)
    {
        SerializedProperty property = serialized.FindPropertyRelative(name);
        if (property == null)
            throw new InvalidOperationException("Serialized value has no field '" + name + "'.");
        return property;
    }

    internal static void Set(SerializedObject value, string field, float data) => Require(value, field).floatValue = data;
    internal static void Set(SerializedObject value, string field, int data) => Require(value, field).intValue = data;
    internal static void Set(SerializedObject value, string field, bool data) => Require(value, field).boolValue = data;
    internal static void SetString(SerializedObject value, string field, string data) => Require(value, field).stringValue = data;
    internal static void Set(SerializedObject value, string field, Vector3 data) => Require(value, field).vector3Value = data;
    internal static void Set(SerializedObject value, string field, Vector2 data) => Require(value, field).vector2Value = data;
    internal static void Set(SerializedObject value, string field, UnityEngine.Object data) => Require(value, field).objectReferenceValue = data;
    internal static void SetCurve(SerializedObject value, string field, AnimationCurve data) => Require(value, field).animationCurveValue = data;
    internal static void Size(SerializedObject value, string field, int count) => Require(value, field).arraySize = count;

    internal static void SetEnum(SerializedObject value, string field, string displayName)
    {
        SerializedProperty property = Require(value, field);
        int index = Array.IndexOf(property.enumDisplayNames, displayName);
        if (index < 0)
            throw new InvalidOperationException(value.targetObject.name + "." + field + " has no enum value '" + displayName + "'.");
        property.enumValueIndex = index;
    }

    internal static void Set(SerializedProperty value, string field, float data) => Require(value, field).floatValue = data;
    internal static void Set(SerializedProperty value, string field, int data) => Require(value, field).intValue = data;
    internal static void Set(SerializedProperty value, string field, bool data) => Require(value, field).boolValue = data;
    internal static void SetString(SerializedProperty value, string field, string data) => Require(value, field).stringValue = data;
    internal static void Set(SerializedProperty value, string field, Vector3 data) => Require(value, field).vector3Value = data;
    internal static void Set(SerializedProperty value, string field, Vector2 data) => Require(value, field).vector2Value = data;
    internal static void Set(SerializedProperty value, string field, UnityEngine.Object data) => Require(value, field).objectReferenceValue = data;
    internal static void SetCurve(SerializedProperty value, string field, AnimationCurve data) => Require(value, field).animationCurveValue = data;
    internal static void Size(SerializedProperty value, string field, int count) => Require(value, field).arraySize = count;

    internal static void SetObjectArray(SerializedObject serialized, string field, UnityEngine.Object[] values)
    {
        SerializedProperty array = Require(serialized, field);
        array.arraySize = values.Length;
        for (int index = 0; index < values.Length; index++)
            array.GetArrayElementAtIndex(index).objectReferenceValue = values[index];
    }

    internal static GameObject Child(Transform parent, string name, Vector3 localPosition)
    {
        GameObject child = new GameObject(name);
        child.layer = parent.gameObject.layer;
        child.transform.SetParent(parent, false);
        child.transform.localPosition = localPosition;
        return child;
    }

    internal static Transform Locator(GameObject visual, string name)
    {
        Transform locator = FindDeep(visual.transform, name);
        if (locator == null)
            throw new InvalidOperationException("The F-117 model is missing locator " + name + ".");
        return locator;
    }

    internal static HingeResult CreateAxisHinge(Transform visual, Transform target, string name)
    {
        Transform parent = visual.parent;
        Vector3 pivot = visual.position;
        Quaternion restRotation = visual.rotation;
        Quaternion delta = target.rotation * Quaternion.Inverse(restRotation);
        delta.ToAngleAxis(out float angle, out Vector3 axis);
        if (angle > 180f)
        {
            // Normalize the long 180..360 degree form to the equivalent signed
            // shortest rotation without inverting the target motion.
            angle = 360f - angle;
            axis = -axis;
        }
        if (axis.sqrMagnitude < 0.5f)
            axis = Vector3.right;
        axis.Normalize();

        // LandingGear assumes its deployed gearHinge has a zero local rotation and
        // declares the wheel broken whenever local X exceeds 10 degrees. Keep the
        // arbitrary model-space axis on a separate parent frame, then animate a
        // zeroed child hinge around that frame's local X axis.
        GameObject axisObject = new GameObject(name + "_Axis");
        axisObject.layer = visual.gameObject.layer;
        Transform axisFrame = axisObject.transform;
        axisFrame.SetParent(parent, false);
        axisFrame.position = pivot;
        axisFrame.rotation = Quaternion.FromToRotation(Vector3.right, axis);

        GameObject hingeObject = new GameObject(name);
        hingeObject.layer = visual.gameObject.layer;
        Transform hinge = hingeObject.transform;
        hinge.SetParent(axisFrame, false);
        hinge.localPosition = Vector3.zero;
        hinge.localRotation = Quaternion.identity;
        visual.SetParent(hinge, true);

        Vector3 motion = axisFrame.InverseTransformVector(target.position - pivot);
        return new HingeResult(hinge, angle, motion);
    }

    // BayDoor writes localEuler Z. LandingGear gear-fold writes localEuler X.
    internal static HingeResult CreateZAxisHinge(Transform visual, Transform target, string name)
    {
        Transform parent = visual.parent;
        Vector3 pivot = visual.position;
        Quaternion restRotation = visual.rotation;
        Quaternion delta = target.rotation * Quaternion.Inverse(restRotation);
        delta.ToAngleAxis(out float angle, out Vector3 axis);
        if (angle > 180f)
        {
            angle = 360f - angle;
            axis = -axis;
        }
        if (axis.sqrMagnitude < 0.5f)
            axis = Vector3.forward;
        axis.Normalize();

        GameObject axisObject = new GameObject(name + "_ZAxis");
        axisObject.layer = visual.gameObject.layer;
        Transform axisFrame = axisObject.transform;
        axisFrame.SetParent(parent, false);
        axisFrame.position = pivot;
        axisFrame.rotation = Quaternion.FromToRotation(Vector3.forward, axis);

        GameObject hingeObject = new GameObject(name);
        hingeObject.layer = visual.gameObject.layer;
        Transform hinge = hingeObject.transform;
        hinge.SetParent(axisFrame, false);
        hinge.localPosition = Vector3.zero;
        hinge.localRotation = Quaternion.identity;
        visual.SetParent(hinge, true);
        return new HingeResult(hinge, angle, Vector3.zero);
    }

    internal static HingeResult CreateClosedXHinge(Transform visual, Transform closed, string name)
    {
        Transform parent = visual.parent;
        Vector3 pivot = visual.position;
        Quaternion closedWorld = closed.rotation;
        Quaternion openWorld = visual.rotation;
        Quaternion delta = openWorld * Quaternion.Inverse(closedWorld);
        delta.ToAngleAxis(out float angle, out Vector3 axis);
        if (angle > 180f)
        {
            angle = 360f - angle;
            axis = -axis;
        }
        if (axis.sqrMagnitude < 0.5f)
            axis = Vector3.right;
        axis.Normalize();

        GameObject axisObject = new GameObject(name + "_Axis");
        axisObject.layer = visual.gameObject.layer;
        Transform axisFrame = axisObject.transform;
        axisFrame.SetParent(parent, false);
        axisFrame.position = pivot;
        axisFrame.rotation = Quaternion.FromToRotation(Vector3.right, axis);

        GameObject hingeObject = new GameObject(name);
        hingeObject.layer = visual.gameObject.layer;
        Transform hinge = hingeObject.transform;
        hinge.SetParent(axisFrame, false);
        hinge.localPosition = Vector3.zero;
        hinge.localRotation = Quaternion.AngleAxis(angle, Vector3.right);
        visual.SetParent(hinge, true);
        return new HingeResult(hinge, angle, Vector3.zero);
    }

    internal static Bounds LocalBounds(Transform root, Renderer[] renderers)
    {
        bool initialized = false;
        Bounds result = default;
        foreach (Renderer renderer in renderers)
        {
            Bounds local = renderer.localBounds;
            for (int x = 0; x < 2; x++)
            for (int y = 0; y < 2; y++)
            for (int z = 0; z < 2; z++)
            {
                Vector3 point = new Vector3(
                    x == 0 ? local.min.x : local.max.x,
                    y == 0 ? local.min.y : local.max.y,
                    z == 0 ? local.min.z : local.max.z);
                point = root.InverseTransformPoint(renderer.transform.TransformPoint(point));
                if (!initialized)
                {
                    result = new Bounds(point, Vector3.zero);
                    initialized = true;
                }
                else
                    result.Encapsulate(point);
            }
        }
        return result;
    }

    internal readonly struct HingeResult
    {
        internal readonly Transform Transform;
        internal readonly float Angle;
        internal readonly Vector3 Motion;

        internal HingeResult(Transform transform, float angle, Vector3 motion)
        {
            Transform = transform;
            Angle = angle;
            Motion = motion;
        }
    }
}
