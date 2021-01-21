import random
import numpy as np


def _get_random_color(pastel_factor=127):
    return [int(((random.randint(0, 255) + pastel_factor) / (255 + pastel_factor)) * 255) for _ in range(3)]


def _color_distance(c1, c2):
    return sum([abs(x[0] - x[1]) for x in zip(c1, c2)])


def generate_new_color(existing_colors=[], pastel_factor=50):
    max_distance = None
    best_color = None
    for i in range(0, 100):
        color = _get_random_color(pastel_factor=pastel_factor)
        if not existing_colors:
            return np.array(color, dtype=np.uint8)
        best_distance = min([_color_distance(color, c) for c in existing_colors])
        if not max_distance or best_distance > max_distance:
            max_distance = best_distance
            best_color = color
    return np.array(best_color, dtype=int)