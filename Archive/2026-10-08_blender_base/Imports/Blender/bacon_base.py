# The bare base body of the main character, from the R15 bacon exported by Roblox Studio (Imports/bacon.obj).
#
# R15 is 15 separate parts pushed deep into each other (a forearm 15 cm up into the upper arm, a shin 23 cm up into
# the thigh), so any bend shows the hidden ends. Here the body parts (all but the head and the hair, which stay
# separate and rigid, Roblox-style) are fused into one closed surface by a voxel remesh, rigged to the same skeleton
# the Unity builder makes, and weighted by Blender's bone heat. To keep limbs from fusing where they only touch, the
# arms are first swung out into an A-pose about the shoulders (ArmOut) and the legs moved apart a little (LegGap).
#
# Writes Imports/bacon_base.json for ObjBaconBuilder: vertices, normals and triangles in the OBJ's own space (studs,
# Y up, facing -Z), the skeleton's joints in the same space, and up to four bone weights per vertex.
#
#   blender -b -P Imports/Blender/bacon_base.py
import bpy, bmesh, json, math, os
from mathutils import Vector

ROOT = os.path.dirname(os.path.dirname(os.path.dirname(os.path.abspath(__file__))))
SOURCE = os.path.join(ROOT, "Imports", "bacon.obj")
OUT = os.path.join(ROOT, "Imports", "bacon_base.json")

S = 0.33            # metres per stud, as in ObjBaconBuilder
ARM_OUT = 35.0      # degrees the arms swing out from hanging
LEG_GAP = 0.01 / S  # each leg moved out sideways, studs
VOXEL = 0.006 / S   # remesh voxel, studs
SMOOTH_FACTOR, SMOOTH_ITERATIONS = 0.5, 14

BODY = {  # OBJ group -> bone, for the parts fused into the body
    "Rig15": "Spine", "Rig14": "Hips",
    "Rig9": "LeftUpperArm", "Rig8": "LeftLowerArm", "Rig7": "LeftHand",
    "Rig13": "RightUpperArm", "Rig12": "RightLowerArm", "Rig11": "RightHand",
    "Rig6": "LeftUpperLeg", "Rig5": "LeftLowerLeg", "Rig4": "LeftFoot",
    "Rig3": "RightUpperLeg", "Rig2": "RightLowerLeg", "Rig1": "RightFoot",
}
SIDES = {"Left": ("Rig9", "Rig8", "Rig7", "Rig6", "Rig5", "Rig4"), "Right": ("Rig13", "Rig12", "Rig11", "Rig3", "Rig2", "Rig1")}
PARENT = {
    "Hips": None, "Spine": "Hips", "Head": "Spine",
    "LeftUpperArm": "Spine", "LeftLowerArm": "LeftUpperArm", "LeftHand": "LeftLowerArm",
    "RightUpperArm": "Spine", "RightLowerArm": "RightUpperArm", "RightHand": "RightLowerArm",
    "LeftUpperLeg": "Hips", "LeftLowerLeg": "LeftUpperLeg", "LeftFoot": "LeftLowerLeg",
    "RightUpperLeg": "Hips", "RightLowerLeg": "RightUpperLeg", "RightFoot": "RightLowerLeg",
}
TAIL = {"Hips": "Spine", "Spine": "Head"}  # each bone's tail at the next joint along (hands and feet: their middle)
for side in ("Left", "Right"):
    TAIL.update({side + "UpperArm": side + "LowerArm", side + "LowerArm": side + "Hand",
                 side + "UpperLeg": side + "LowerLeg", side + "LowerLeg": side + "Foot"})

# ---------------------------------------------------------------- the OBJ, in its own space

verts, faces = [], {}
group = "default"
for line in open(SOURCE):
    p = line.split()
    if not p:
        continue
    if p[0] == "v":
        verts.append(Vector((float(p[1]), float(p[2]), float(p[3]))))
    elif p[0] in ("g", "o"):
        group = p[1] if len(p) > 1 else "default"
    elif p[0] == "f":
        faces.setdefault(group, []).append([int(c.split("/")[0]) - 1 for c in p[1:]])

def bounds(g):
    pts = [verts[i] for f in faces[g] for i in f]
    lo = Vector((min(v.x for v in pts), min(v.y for v in pts), min(v.z for v in pts)))
    hi = Vector((max(v.x for v in pts), max(v.y for v in pts), max(v.z for v in pts)))
    return lo, hi, (lo + hi) / 2

center_x = (min(v.x for v in verts) + max(v.x for v in verts)) / 2

# Joints, as ObjBaconBuilder places them (its metre offsets turned into studs)
joints = {}
for side, (ua, la, hd, ul, ll, ft) in SIDES.items():
    (ua_lo, ua_hi, ua_c), (la_lo, la_hi, la_c), (hd_lo, hd_hi, hd_c) = bounds(ua), bounds(la), bounds(hd)
    (ul_lo, ul_hi, ul_c), (ll_lo, ll_hi, ll_c), (ft_lo, ft_hi, ft_c) = bounds(ul), bounds(ll), bounds(ft)
    joints[side + "UpperArm"] = Vector((ua_c.x, ua_hi.y - 0.09 / S, ua_c.z))
    joints[side + "LowerArm"] = Vector((la_c.x, (ua_lo.y + la_hi.y) / 2, la_c.z))
    joints[side + "Hand"] = Vector((hd_c.x, (la_lo.y + hd_hi.y) / 2, la_c.z))
    joints[side + "UpperLeg"] = Vector((ul_c.x, ul_hi.y - 0.08 / S, ul_c.z))
    joints[side + "LowerLeg"] = Vector((ll_c.x, (ul_lo.y + ll_hi.y) / 2, ll_c.z))
    joints[side + "Foot"] = Vector((ll_c.x, (ll_lo.y + ft_hi.y) / 2, ll_c.z))
lt_lo, lt_hi, lt_c = bounds("Rig14")
ut_lo, ut_hi, ut_c = bounds("Rig15")
hd_lo, hd_hi, hd_c = bounds("Rig10")
joints["Hips"] = Vector((center_x, joints["LeftUpperLeg"].y, lt_c.z))
joints["Spine"] = Vector((center_x, (lt_hi.y + ut_lo.y) / 2, ut_c.z))
joints["Head"] = Vector((center_x, (ut_hi.y + hd_lo.y) / 2, hd_c.z))
ends = {}  # where the hands and feet point, for their bones' tails
for side, (ua, la, hd, ul, ll, ft) in SIDES.items():
    ends[side + "Hand"] = bounds(hd)[2]
    ends[side + "Foot"] = bounds(ft)[2]

# ---------------------------------------------------------------- A-pose and leg gap, in OBJ space

def swing(p, pivot, degrees):  # about the OBJ's Z (its forward axis) through pivot
    a = math.radians(degrees)
    d = p - pivot
    return pivot + Vector((d.x * math.cos(a) - d.y * math.sin(a), d.x * math.sin(a) + d.y * math.cos(a), d.z))

moved = list(verts)
for side, (ua, la, hd, ul, ll, ft) in SIDES.items():
    out = -1.0 if side == "Left" else 1.0  # the character's left is the OBJ's -X
    shoulder = joints[side + "UpperArm"].copy()
    arm = {i for g in (ua, la, hd) for f in faces[g] for i in f}
    for i in arm:
        moved[i] = swing(verts[i], shoulder, out * ARM_OUT)
    for name in ("LowerArm", "Hand"):
        joints[side + name] = swing(joints[side + name], shoulder, out * ARM_OUT)
    ends[side + "Hand"] = swing(ends[side + "Hand"], shoulder, out * ARM_OUT)
    leg = {i for g in (ul, ll, ft) for f in faces[g] for i in f}
    for i in leg:
        moved[i] = verts[i] + Vector((out * LEG_GAP, 0, 0))
    for name in ("UpperLeg", "LowerLeg", "Foot"):
        joints[side + name] += Vector((out * LEG_GAP, 0, 0))
    ends[side + "Foot"] += Vector((out * LEG_GAP, 0, 0))

# OBJ (x, y, z), Y up, to Blender (x, -z, y), Z up: a rotation, so windings keep their sense
def to_b(p):
    return Vector((p.x, -p.z, p.y))

def from_b(p):
    return Vector((p.x, p.z, -p.y))

# ---------------------------------------------------------------- fuse

bpy.ops.wm.read_factory_settings(use_empty=True)
scene = bpy.context.scene
used = sorted({i for g in BODY for f in faces[g] for i in f})
index = {old: new for new, old in enumerate(used)}
mesh = bpy.data.meshes.new("Body")
mesh.from_pydata([to_b(moved[i]) for i in used], [], [[index[i] for i in f] for g in BODY for f in faces[g]])
mesh.validate()
body = bpy.data.objects.new("Body", mesh)
scene.collection.objects.link(body)

remesh = body.modifiers.new("Remesh", "REMESH")
remesh.mode = "VOXEL"
remesh.voxel_size = VOXEL
remesh.adaptivity = 0.0
remesh.use_smooth_shade = True
# The voxels leave fine terraces, and the R15 limbs were faceted cylinders: a light smoothing evens both out without
# losing the blocky shapes
smooth = body.modifiers.new("Smooth", "SMOOTH")
smooth.factor = SMOOTH_FACTOR
smooth.iterations = SMOOTH_ITERATIONS
depsgraph = bpy.context.evaluated_depsgraph_get()
fused = bpy.data.meshes.new_from_object(body.evaluated_get(depsgraph))
body.modifiers.clear()
body.data = fused
print(f"[bacon_base] fused: {len(fused.vertices)} vertices, {len(fused.polygons)} faces")

# ---------------------------------------------------------------- rig and weights

armature_data = bpy.data.armatures.new("Rig")
rig = bpy.data.objects.new("Rig", armature_data)
scene.collection.objects.link(rig)
bpy.context.view_layer.objects.active = rig
bpy.ops.object.mode_set(mode="EDIT")
for name in PARENT:  # parents come before their children in PARENT
    b = armature_data.edit_bones.new(name)
    b.head = to_b(joints[name])
    if name in ends:
        tail = ends[name]
    elif name == "Head":
        tail = joints["Head"] + Vector((0, 0.8, 0))
    else:
        tail = joints[TAIL[name]]
    b.tail = to_b(tail)
    if PARENT[name]:
        b.parent = armature_data.edit_bones[PARENT[name]]
    b.use_deform = name != "Head"  # the head is its own rigid mesh
bpy.ops.object.mode_set(mode="OBJECT")

bpy.ops.object.select_all(action="DESELECT")
body.select_set(True)
rig.select_set(True)
bpy.context.view_layer.objects.active = rig
bpy.ops.object.parent_set(type="ARMATURE_AUTO")
missing = [b for b in PARENT if b != "Head" and b not in body.vertex_groups]
if missing:
    raise SystemExit(f"[bacon_base] bone heat left these bones without weights: {missing}")

# ---------------------------------------------------------------- write

bm = bmesh.new()
bm.from_mesh(body.data)
bmesh.ops.triangulate(bm, faces=bm.faces[:])
bm.to_mesh(body.data)
bm.free()
me = body.data
bones = [b for b in PARENT if b != "Head"]
groups = {g.index: g.name for g in body.vertex_groups}
v_out, n_out, w_out, b_out = [], [], [], []
for v in me.vertices:
    p = from_b(v.co)
    n = from_b(v.normal)
    v_out += [p.x, p.y, p.z]
    n_out += [n.x, n.y, n.z]
    ws = sorted(((g.weight, groups[g.group]) for g in v.groups if g.weight > 0 and groups[g.group] in bones), reverse=True)[:4]
    total = sum(w for w, _ in ws) or 1.0
    ws += [(0.0, bones[0])] * (4 - len(ws))
    for w, name in ws:
        w_out.append(round(w / total, 5))
        b_out.append(bones.index(name))
t_out = [i for poly in me.polygons for i in poly.vertices]
data = {
    "armOut": ARM_OUT,
    "bones": list(PARENT),
    "joints": [c for name in PARENT for c in joints[name]],
    "skinBones": bones,
    "v": [round(c, 5) for c in v_out],
    "n": [round(c, 4) for c in n_out],
    "t": t_out,
    "weights": w_out,
    "weightBones": b_out,
}
with open(OUT, "w") as f:
    json.dump(data, f, separators=(",", ":"))
print(f"[bacon_base] wrote {OUT}: {len(me.vertices)} vertices, {len(t_out) // 3} triangles")
