import json
import _pickle
from functools import lru_cache
from pprint import pprint
import numpy as np
import sys
import os
import cv2
from colors import generate_new_color
from gta_math import *


def draw_3d_bbox(image, anno, view_matrix, proj_matrix, color):
    height, width, _ = image.shape
    bbox_3d_screen = []
    for p in anno["Bbox3d"]['CornerPoints']:
        p_world = (p["X"], p["Y"], p["Z"])
        pixel_pos = world_coords_to_pixel(p_world, view_matrix, proj_matrix, width, height)
        screen_x, screen_y = pixel_pos.astype(int)
        ndc_y, ndc_x = pixel_to_ndc((screen_y, screen_x), (height, width))
        if ndc_x < -1 or ndc_x > 1 or ndc_y < -1 or ndc_y > 1:
            continue
        bbox_3d_screen.append((screen_x, screen_y))
    for i in range(len(bbox_3d_screen)):
        for j in range(len(bbox_3d_screen)):
            if i == j:
                continue
            image = cv2.line(image, bbox_3d_screen[i], bbox_3d_screen[j], color, 2)


def draw_2d_bbox(image, anno, color):
    bbox = anno["Bbox2d"]
    x = int(min(width - 1, round(bbox["Min"]["X"] * width)))
    y = int(min(height - 1, round(bbox["Min"]["Y"] * height)))
    x2 = int(min(width - 1, round(bbox["Max"]["X"] * width)))
    y2 = int(min(height - 1, round(bbox["Max"]["Y"] * height)))
    # bbox = np.array([[x, y], [x2, y2]])
    image = cv2.rectangle(image, (x, y), (x2, y2), color, thickness=3)


# use bones to create very tight bboxes
def draw_tight_bbox(image, anno, color):
    height, width, _ = image.shape
    bbox_3d_screen = []
    for p in anno["Bones3D"].values():
        p_world = p["X"], p["Y"], p["Z"]
        pixel_pos = world_coords_to_pixel(p_world, view_matrix, proj_matrix, width, height)
        screen_x, screen_y = pixel_pos.astype(int)
        ndc_y, ndc_x = pixel_to_ndc((screen_y, screen_x), (height, width))
        if ndc_x < -1 or ndc_x > 1 or ndc_y < -1 or ndc_y > 1:
            continue
        bbox_3d_screen.append((screen_x, screen_y))
    x, y, w, h = cv2.boundingRect(np.array(bbox_3d_screen))
    image = cv2.rectangle(image, (x, y), (x + w, y + h), color, thickness=3)


def draw_3d_bones(image, anno, view_matrix, proj_matrix, color):
    height, width, _ = image.shape
    for p in anno["Bones3D"].values():
        visible = p["IsVisible"]
        material = p["material"]
        p = p["Pos"]
        p_world = p["X"], p["Y"], p["Z"]

        pixel_pos = world_coords_to_pixel(p_world, view_matrix, proj_matrix, width, height)
        screen_x, screen_y = pixel_pos.astype(int)
        ndc_y, ndc_x = pixel_to_ndc((screen_y, screen_x), (height, width))
        if ndc_x < -1 or ndc_x > 1 or ndc_y < -1 or ndc_y > 1:
            continue
        b = 0
        r = 0
        if visible == 1:
            g = 127
        elif visible == 0:
            g = 255
        else:
            g = 0
            print(material)


        image = cv2.circle(image, (screen_x, screen_y), 2, (b, g, r), 2)
        # else:
        #     image = cv2.circle(image, (screen_x, screen_y), 2, (0, 0, 255), 2)


if __name__ == '__main__':
    location_data = json.load(open(r'F:\datasets\GTA_V_anomaly\locations_processed\0.json'))
    # proj_matrix2 = np.array(location_data["ProjectionMatrix"]["Values"]).reshape((4, 4)).T

    # contruct projection matrix since one from file has numerical stability issues
    fov = location_data["CameraFOV"]
    near_clip = location_data['CameraNearClip']
    far_clip = location_data['CameraFarClip']
    proj_matrix = construct_proj_matrix(1440, 2560, fov, near_clip, far_clip)

    # load view matrix from file and set small values to 0
    view_matrix = np.array(location_data["ViewMatrix"]["Values"]).reshape((4, 4)).T  # Transpose: c# saves columns wise
    loc = np.logical_and(view_matrix < 0.000001,
                         view_matrix > 0)
    view_matrix[loc] = 0

    loc = np.logical_and(view_matrix > -0.000001,
                         view_matrix < 0)
    view_matrix[loc] = 0
    # world_matrix2 = np.array(location_data["WorldMatrix"]["Values"]).reshape((4, 4)).T

    # path to some data
    path = r'F:\datasets\GTA_V_anomaly\data\0'
    # find all annotation files
    files = [i for i in os.listdir(path) if i.endswith(".json") and not i.startswith(".DS")]
    files.sort()  # make it sorted video
    length = len(files)

    handle_to_color = {}
    colors_used = []
    cv2.namedWindow("image", cv2.WINDOW_KEEPRATIO)

    for file in files:
        json_path = f"{path}/{file}"
        image_path = json_path.replace(".json", ".tiff")
        image_ori = cv2.imread(image_path)
        image = image_ori.copy()

        stencil_path = json_path.replace(".json", "-stencil.tiff")
        stencil = cv2.imread(stencil_path, cv2.IMREAD_GRAYSCALE)

        person_mask = np.bitwise_and(stencil, 7) == 1  # ped_stencil_id = 1

        car_mask = np.bitwise_and(stencil, 7) == 2  # vehicle_stencil_id = 2

        depth_path = json_path.replace(".json", "-depth.tiff")
        depth = cv2.imread(depth_path, -1)
        height, width = depth.shape

        # load depth image and convert to meters using proj_matrix, then make it positive and clip far
        depth = ndc_to_real(depth, proj_matrix)
        depth = depth_crop_and_positive(depth)

        annotations = json.load(open(json_path, "r"))["Detections"]
        annotations_sorted = sorted(annotations, key=lambda x: int(x["Distance"]), reverse=True)  # process closest first
        for anno in annotations_sorted:
            handle = anno["Handle"]
            distance = int(anno["Distance"])
            is_person = anno["Type"] == 1
            if is_person:
                mask = person_mask
            else:
                continue

            if handle in handle_to_color:
                color = handle_to_color[handle]
            else:
                color = generate_new_color(colors_used)
                colors_used.append(color)
                color = (int(color[0]), int(color[1]), int(color[2]))
                handle_to_color[handle] = color

            # draw_3d_bbox(image, anno, view_matrix, proj_matrix, color)
            draw_3d_bones(image, anno, view_matrix, proj_matrix, color)
            # draw_2d_bbox(image, anno, color)
            # draw_tight_bbox(image, anno, color)

            # TODO make "check_occluded" code!
        cv2.imshow("image", image)
        cv2.waitKey(0)
