import _pickle
from functools import lru_cache
import numpy as np
from math import tan, radians, sin, cos, sqrt

PROJECTING = False
THRESHOLD = 1000
MAXIMUM = np.iinfo(np.uint16).max


# Borrowing code from https://github.com/racinmat/GTAVisionExport-postprocessing all credits to him :)

@lru_cache(maxsize=8)
def get_pixels_meshgrid(width, height):
    # this shall be called per entity in image, this saves the data
    cc, rr = np.meshgrid(range(width), range(height))
    return cc, rr


@lru_cache(maxsize=8)
def get_pixels_3d_cached(depth, proj_matrix, view_matrix, width, height):
    # _pickle should be pickle in C, thus faster
    depth = _pickle.loads(depth)
    proj_matrix = _pickle.loads(proj_matrix)
    view_matrix = _pickle.loads(view_matrix)
    return get_pixels_3d(depth, proj_matrix, view_matrix, width, height)


def pixel_to_ndc(pixel, size):
    p_y, p_x = pixel
    s_y, s_x = size
    s_y -= 1  # so 1 is being mapped into (n-1)th pixel
    s_x -= 1  # so 1 is being mapped into (n-1)th pixel
    return (- 2 / s_y) * p_y + 1, (2 / s_x) * p_x - 1


def get_pixels_3d(depth, proj_matrix, view_matrix, width, height):
    data = {
        'width': width,
        'height': height,
        'proj_matrix': proj_matrix
    }
    # this shall be called per entity in image, this saves the data
    pts, _ = points_to_homo(data, depth, tresholding=False)  # False to get all pixels
    pts_p = ndc_to_view(pts, proj_matrix)
    pixel_3d = view_to_world(pts_p, view_matrix)
    pixel_3d = np.reshape(pixel_3d, (4, height, width))
    pixel_3d = pixel_3d[0:3, ::]
    return pixel_3d


def points_to_homo(res, depth, tresholding=True):
    width = res['width']
    height = res['height']
    size = (height, width)
    proj_matrix = res['proj_matrix']

    if tresholding:
        # max_depth = res['cam_far_clip']
        max_depth = 60  # just for testing
        vec = proj_matrix @ np.array([[1], [1], [-max_depth], [1]])
        # print(vec)
        vec /= vec[3]
        threshold = vec[2]
    else:
        threshold = - np.inf

    if PROJECTING:
        # print('projecting')
        depth[
            depth < threshold] = threshold  # since 0 is far clip, depth below threshold is behind threshold, and this projects it
    # print('threshold', threshold)
    # vecs = np.zeros((4, points.shape[0]))
    valid_points = np.where(depth >= threshold)
    valid_y, valid_x = valid_points

    vecs = np.zeros((4, len(valid_y)))

    ndcs = pixels_to_ndcs(np.array(valid_points), size)

    vecs[0, :] = ndcs[1, :]
    vecs[1, :] = ndcs[0, :]
    vecs[2, :] = depth[valid_y, valid_x]
    vecs[3, :] = 1  # last, homogenous coordinate
    return vecs, valid_points


def ndc_to_view(vecs, proj_matrix):
    vecs_p = np.linalg.inv(proj_matrix) @ vecs
    vecs_p /= vecs_p[3, :]
    return vecs_p


def view_to_world(vecs_p, view_matrix):
    vecs_p = inv_rigid(view_matrix) @ vecs_p
    vecs_p /= vecs_p[3, :]
    return vecs_p


def is_rotation_matrix(R):
    Rt = np.transpose(R)
    should_be_identity = np.dot(Rt, R)
    I = np.identity(3, dtype=R.dtype)
    n = np.linalg.norm(I - should_be_identity)
    return n < 1e-6


def is_rigid(M):
    return is_rotation_matrix(M[0:3, 0:3]) and np.linalg.norm(M[3, :] - np.array([0, 0, 0, 1], dtype=M.dtype)) < 1e-6


def inv_rigid(M):
    # if we have rigid transformation matrix, we can calculate its inversion analytically, with bigger precision
    assert is_rigid(M)
    Mt = np.zeros_like(M)
    Mt[0:3, 0:3] = np.transpose(M[0:3, 0:3])
    Mt[0:3, 3] = - Mt[0:3, 0:3] @ M[0:3, 3]
    Mt[3, 3] = 1
    return Mt


def ndc_to_real(depth, proj_matrix):
    width = depth.shape[1]
    height = depth.shape[0]

    params = {
        'width': width,
        'height': height,
        'proj_matrix': proj_matrix,
    }

    vecs, transformed_points = points_to_homo(params, depth, tresholding=False)
    vec_y, vec_x = transformed_points

    vecs_p = ndc_to_view(vecs, proj_matrix).T

    new_depth = np.copy(depth)
    new_depth[vec_y, vec_x] = vecs_p[:, 2]

    return new_depth


def depth_crop_and_positive(depth):
    depth = np.copy(depth)
    # first we reverse values, so they are in positive values
    depth *= -1
    # then we treshold the far clip so when we scale to integer range
    depth[depth > THRESHOLD] = THRESHOLD
    return depth


def depth_to_integer_range(depth):
    depth = np.copy(depth)
    # then we rescale to as big value as file format allows us
    ratio = MAXIMUM / THRESHOLD
    depth *= ratio
    return depth.astype(np.int32)


def depth_from_integer_range(depth):
    depth = np.copy(depth)
    depth = depth.astype(np.float32)
    # then we rescale to integer32
    ratio = THRESHOLD / MAXIMUM
    depth *= ratio
    return depth


def construct_proj_matrix(H=1080, W=1914, fov=50.0, near_clip=1.5, far_clip=None):
    nc = near_clip
    if far_clip is None:
        fc = get_gta_far_clip()
    else:
        fc = far_clip
    fov_r = radians(fov)
    tan_fov_r_two = tan(fov_r / 2)
    nc_min_fc = nc - fc

    x00 = H / (W * tan_fov_r_two)
    x11 = 1 / tan_fov_r_two
    x22 = -nc / nc_min_fc
    x23 = (-nc * fc) / nc_min_fc

    return np.array([
        [x00, 0,   0, 0],
        [0,   x11, 0, 0],
        [0,   0,   x22, x23],
        [0,   0,   -1, 0],
    ])
    # return np.array([
    #     [x00, 0, 0, 0],
    #     [0, x11, 0, 0],
    #     [0, 0, x22, -1],
    #     [0, 0, x23, 0],
    # ])


def get_gta_far_clip():
    return 10003.815  # the far clip, rounded value of median, after very weird values were discarded


def pixels_to_ndcs(pixels, size):
    # vectorized version, of above function
    pixels = np.copy(pixels).astype(np.float32)
    if pixels.shape[1] == 2 and pixels.shape[0] != 2:
        pixels = pixels.T
    # pixels are in shape <pixels, 2>
    p_y = pixels[0, :]
    p_x = pixels[1, :]
    s_y, s_x = size
    s_y -= 1  # so 1 is being mapped into (n-1)th pixel
    s_x -= 1  # so 1 is being mapped into (n-1)th pixel
    pixels[0, :] = (-2 / s_y) * p_y + 1
    pixels[1, :] = (2 / s_x) * p_x - 1
    return pixels


def are_behind_plane(x0, x1, x2, x, y, z):
    v1 = x1 - x0
    v2 = x2 - x0
    n = np.cross(v1, v2)
    return n[0] * (x - x0[0]) + n[1] * (y - x0[1]) + n[2] * (z - x0[2]) > 0


def get_pixels_inside_3d_bbox_mask(bbox, car_mask, depth, bbox_3d_world_homo, proj_matrix, view_matrix, width, height):
    # instance segmentation, for vehicle stencil id pixels, checking if depth pixels are inside 3d bbox in world
    #  coordinates and comparing number of these depth pixels in and outside 3d bbox to determine the visibility
    cc, rr = get_pixels_meshgrid(width, height)
    # _pickle is C implementation of pickle, very fast. This is the best and fastest way to serialize and
    # deserialize numpy arrays. Thus great for caching
    pixel_3d = get_pixels_3d_cached(_pickle.dumps(depth), _pickle.dumps(proj_matrix), _pickle.dumps(view_matrix), width,
                                    height)
    # pixel_3d = get_pixels_3d(depth, proj_matrix, view_matrix, width, height)      # non cached version, slower

    bbox_3d_world_homo /= bbox_3d_world_homo[3, :]
    bbox_3d_world = bbox_3d_world_homo[0:3, :].T

    # points inside the 2D bbox with car mask on
    car_pixels_mask = (car_mask == True) & (cc >= bbox[1, 0]) & (cc <= bbox[0, 0]) & (rr >= bbox[1, 1]) & (
            rr <= bbox[0, 1])
    # must be == True, because this operator is overloaded to compare every element with True value
    idxs = np.where(car_pixels_mask)

    # 3D coordinates of pixels in idxs
    x = pixel_3d[0, ::].squeeze()[idxs]
    y = pixel_3d[1, ::].squeeze()[idxs]
    z = pixel_3d[2, ::].squeeze()[idxs]

    # test if the points lie inside 3D bbox
    in1 = are_behind_plane(bbox_3d_world[3, :], bbox_3d_world[2, :], bbox_3d_world[7, :], x, y, z)
    in2 = are_behind_plane(bbox_3d_world[1, :], bbox_3d_world[5, :], bbox_3d_world[0, :], x, y, z)
    in3 = are_behind_plane(bbox_3d_world[6, :], bbox_3d_world[2, :], bbox_3d_world[4, :], x, y, z)
    in4 = are_behind_plane(bbox_3d_world[3, :], bbox_3d_world[7, :], bbox_3d_world[1, :], x, y, z)
    in5 = are_behind_plane(bbox_3d_world[7, :], bbox_3d_world[6, :], bbox_3d_world[5, :], x, y, z)
    in6 = are_behind_plane(bbox_3d_world[0, :], bbox_3d_world[2, :], bbox_3d_world[1, :], x, y, z)
    is_inside = in1 & in2 & in3 & in4 & in5 & in6
    return is_inside


def homo_world_coords_to_pixel(point_homo, view_matrix, proj_matrix, width, height):
    viewed = view_matrix @ point_homo
    projected = proj_matrix @ viewed
    # following row works for partially visible cars, not for cars completely outside of the frustum
    # this row is very important. It fixed invalid projection of points outside the camera view frustum
    projected[0:3, projected[3] < 0] *= -1
    projected /= projected[3]
    to_pixel_matrix = np.array([
        [width / 2, 0, 0, width / 2],
        [0, -height / 2, 0, height / 2],
    ])
    in_pixels = to_pixel_matrix @ projected
    return in_pixels


def world_coords_to_pixel(pos, view_matrix, proj_matrix, width, height):
    point_homo = np.array([pos[0], pos[1], pos[2], 1])
    return homo_world_coords_to_pixel(point_homo, view_matrix, proj_matrix, width, height)


def min_max_norm(img):
    min_val = img.min()
    max_val = img.max()
    if min_val == max_val:
        return img
    return (img - img.min()) / (img.max() - img.min())


def create_model_rot_matrix(euler):
    x = np.radians(euler[0])
    y = np.radians(euler[1])
    z = np.radians(euler[2])

    Rx = np.array([
        [1, 0, 0],
        [0, cos(x), -sin(x)],
        [0, sin(x), cos(x)]
    ], dtype=np.float)
    Ry = np.array([
        [cos(y), 0, sin(y)],
        [0, 1, 0],
        [-sin(y), 0, cos(y)]
    ], dtype=np.float)
    Rz = np.array([
        [cos(z), -sin(z), 0],
        [sin(z), cos(z), 0],
        [0, 0, 1]
    ], dtype=np.float)
    result = Rx @ Ry @ Rz
    return result


def construct_model_matrix(position, rotation):
    view_matrix = np.zeros((4, 4))
    # view_matrix[0:3, 3] = camera_pos
    view_matrix[0:3, 0:3] = create_model_rot_matrix(rotation)
    view_matrix[0:3, 3] = position
    view_matrix[3, 3] = 1
    return view_matrix


def euclid_distance(pt1, pt2):
    assert len(pt1) == len(pt2), f"Both points should be equal in length; {len(pt1)} vs {len(pt2)}"
    return sqrt(sum([(i-j) ** 2 for i, j in zip(pt1, pt2)]))