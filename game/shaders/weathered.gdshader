shader_type spatial;
render_mode cull_back, diffuse_burley, specular_schlick_ggx;

// Surfaces for the level kit. Until a material has textures (kit/materials.jsonc), this draws a
// procedural pattern in world space: concrete, block, brick, peeling plaster, corrugated sheet,
// painted or rusted metal, asphalt, dirt, gravel, wood, tarp, rubber, grass. Weathering adds grime
// at the foot of walls, rain streaks and moss, so the compound looks abandoned rather than new.
// Mesh contract (LevelBuilder): UV = world-space metres along the face's axes (v up on walls),
// UV2 = (height above the piece's base, piece height).
// Colour literals below are sRGB, converted with srgb(); multipliers are plain factors.

uniform vec3 base_color : source_color = vec3(0.6);
uniform int pattern = 0;
uniform float tile_m = 2.0;
uniform float roughness_base : hint_range(0.0, 1.0) = 0.9;
uniform float metallic_base : hint_range(0.0, 1.0) = 0.0;
uniform float weathering : hint_range(0.0, 1.0) = 0.5;
uniform bool use_textures = false;
uniform bool has_normal_tex = false;
uniform bool has_roughness_tex = false;
uniform sampler2D albedo_tex : source_color, filter_linear_mipmap_anisotropic, repeat_enable;
uniform sampler2D normal_tex : hint_normal, filter_linear_mipmap_anisotropic, repeat_enable;
uniform sampler2D roughness_tex : hint_roughness_r, filter_linear_mipmap_anisotropic, repeat_enable;

const int PLAIN = 0;
const int CONCRETE = 1;
const int BLOCK = 2;
const int BRICK = 3;
const int PLASTER = 4;
const int CORRUGATED = 5;
const int METAL = 6;
const int RUST = 7;
const int ASPHALT = 8;
const int DIRT = 9;
const int GRAVEL = 10;
const int WOOD = 11;
const int TARP = 12;
const int RUBBER = 13;
const int GLASS = 14;
const int GRASS = 15;

varying vec3 world_pos;
varying vec3 world_normal;

void vertex() {
	world_pos = (MODEL_MATRIX * vec4(VERTEX, 1.0)).xyz;
	world_normal = normalize((MODEL_MATRIX * vec4(NORMAL, 0.0)).xyz);
}

vec3 srgb(vec3 c) {
	return mix(c / 12.92, pow((c + 0.055) / 1.055, vec3(2.4)), step(vec3(0.04045), c));
}

// Hash functions without sine (Dave Hoskins), stable across GPUs.
float hash12(vec2 p) {
	vec3 p3 = fract(vec3(p.xyx) * 0.1031);
	p3 += dot(p3, p3.yzx + 33.33);
	return fract((p3.x + p3.y) * p3.z);
}

vec2 hash22(vec2 p) {
	vec3 p3 = fract(vec3(p.xyx) * vec3(0.1031, 0.1030, 0.0973));
	p3 += dot(p3, p3.yzx + 33.33);
	return fract((p3.xx + p3.yz) * p3.zy);
}

float hash13(vec3 p3) {
	p3 = fract(p3 * 0.1031);
	p3 += dot(p3, p3.zyx + 31.32);
	return fract((p3.x + p3.y) * p3.z);
}

float noise2(vec2 p) {
	vec2 i = floor(p);
	vec2 f = fract(p);
	vec2 u = f * f * (3.0 - 2.0 * f);
	return mix(mix(hash12(i), hash12(i + vec2(1.0, 0.0)), u.x),
		mix(hash12(i + vec2(0.0, 1.0)), hash12(i + vec2(1.0, 1.0)), u.x), u.y);
}

float noise3(vec3 p) {
	vec3 i = floor(p);
	vec3 f = fract(p);
	vec3 u = f * f * (3.0 - 2.0 * f);
	float a = mix(hash13(i), hash13(i + vec3(1.0, 0.0, 0.0)), u.x);
	float b = mix(hash13(i + vec3(0.0, 1.0, 0.0)), hash13(i + vec3(1.0, 1.0, 0.0)), u.x);
	float c = mix(hash13(i + vec3(0.0, 0.0, 1.0)), hash13(i + vec3(1.0, 0.0, 1.0)), u.x);
	float d = mix(hash13(i + vec3(0.0, 1.0, 1.0)), hash13(i + vec3(1.0, 1.0, 1.0)), u.x);
	return mix(mix(a, b, u.y), mix(c, d, u.y), u.z);
}

float fbm2(vec2 p) {
	float v = 0.0;
	float a = 0.5;
	for (int i = 0; i < 4; i++) {
		v += a * noise2(p);
		p = p * 2.07 + vec2(13.1, 7.7);
		a *= 0.5;
	}
	return v;
}

float fbm3(vec3 p) {
	float v = 0.0;
	float a = 0.5;
	for (int i = 0; i < 4; i++) {
		v += a * noise3(p);
		p = p * 2.03 + vec3(17.1, 9.2, 4.3);
		a *= 0.5;
	}
	return v;
}

// Distance to the nearest cell feature and that feature's cell id.
vec3 worley(vec2 p) {
	vec2 i = floor(p);
	vec2 f = fract(p);
	float best = 8.0;
	vec2 id = vec2(0.0);
	for (int y = -1; y <= 1; y++) {
		for (int x = -1; x <= 1; x++) {
			vec2 o = vec2(float(x), float(y));
			vec2 r = o + hash22(i + o) - f;
			float d = dot(r, r);
			if (d < best) {
				best = d;
				id = i + o;
			}
		}
	}
	return vec3(sqrt(best), id);
}

// Distance, in cell units, to the nearest border between Voronoi cells (Inigo Quilez's two-pass
// method). The borders form a polygonal network, like cracked asphalt or concrete.
float voronoi_border(vec2 x) {
	vec2 n = floor(x);
	vec2 f = fract(x);
	vec2 mg = vec2(0.0);
	vec2 mr = vec2(0.0);
	float md = 8.0;
	for (int j = -1; j <= 1; j++) {
		for (int i = -1; i <= 1; i++) {
			vec2 g = vec2(float(i), float(j));
			vec2 r = g + hash22(n + g) - f;
			float d = dot(r, r);
			if (d < md) {
				md = d;
				mr = r;
				mg = g;
			}
		}
	}
	md = 8.0;
	for (int j = -2; j <= 2; j++) {
		for (int i = -2; i <= 2; i++) {
			vec2 g = mg + vec2(float(i), float(j));
			vec2 r = g + hash22(n + g) - f;
			vec2 dr = r - mr;
			if (dot(dr, dr) > 1e-5) {
				md = min(md, dot(0.5 * (mr + r), normalize(dr)));
			}
		}
	}
	return md;
}

// 1 while a feature of `size_m` spans a few pixels, fading to 0 once it's smaller than a pixel, so
// fine detail turns into its average colour in the distance instead of shimmering.
float detail_fade(float px_m, float size_m) {
	return 1.0 - smoothstep(size_m * 0.5, size_m * 2.0, px_m);
}

// A line `width_m` wide at distance `d_m` from its centre; lines thinner than a pixel get fainter
// rather than wider.
float thin_line(float d_m, float width_m, float px_m) {
	float w = max(width_m, px_m);
	return (1.0 - smoothstep(w * 0.5, w, d_m)) * (width_m / w);
}

// Cracks: a wandering, jagged Voronoi network about `cell_m` across, broken up so only some of its
// edges are cracked.
float crack_network(vec2 m, float cell_m, float width_m, float px_m, float seed) {
	vec2 x = m / cell_m;
	x += (vec2(noise2(m * 0.35 + seed), noise2(m * 0.35 + seed + 5.6)) - 0.5) * 0.5;
	x += (vec2(noise2(m * 9.0 + seed), noise2(m * 9.0 + seed + 4.2)) - 0.5) * (0.03 / cell_m);
	float line = thin_line(voronoi_border(x) * cell_m, width_m, px_m);
	return line * smoothstep(0.4, 0.56, noise2(m * 0.55 + seed + 17.0));
}

// Running-bond masonry: returns (mortar mask, unit variation) for units of `cell` metres. Once the
// courses get too fine for the pixel footprint `px`, the joints fade to their average coverage.
vec2 masonry(vec2 m, vec2 cell, float mortar, float px) {
	vec2 q = m / cell;
	float row = floor(q.y);
	q.x += mod(row, 2.0) * 0.5;
	vec2 f = fract(q);
	vec2 edge = min(f, 1.0 - f) * cell;
	float mm = 1.0 - smoothstep(mortar * 0.45, mortar, min(edge.x, edge.y));
	float coverage = 1.0 - (1.0 - 1.45 * mortar / cell.x) * (1.0 - 1.45 * mortar / cell.y);
	mm = mix(coverage, mm, 1.0 - smoothstep(cell.y / 6.0, cell.y / 2.5, px));
	return vec2(mm, hash12(vec2(floor(q.x), row)));
}

vec3 brick_color(vec2 m, float fine, float px, out float mortar_mask) {
	vec2 b = masonry(m, vec2(0.225, 0.075), 0.010, px);
	mortar_mask = b.x;
	vec3 brick = base_color * (0.72 + 0.45 * b.y);
	brick = mix(brick, brick * vec3(1.08, 0.92, 0.85), hash12(vec2(b.y * 91.0, 3.0)) * 0.5);
	brick = mix(brick, brick * 0.6, step(0.93, hash12(vec2(b.y * 37.0, 11.0)))); // over-fired bricks
	vec3 mortar = srgb(vec3(0.56, 0.54, 0.50)) * (0.85 + 0.25 * fine);
	return mix(brick * (0.9 + 0.2 * fine), mortar, b.x);
}

void surface_pattern(vec2 m, vec3 wp, float px, out vec3 col, out float rough, out float metal, out float h) {
	vec3 c = base_color;
	col = c;
	rough = roughness_base;
	metal = metallic_base;
	h = 0.0;
	float large = fbm3(wp * 0.35);
	float fine = noise3(wp * 9.0);
	vec3 rust_col = srgb(vec3(0.42, 0.25, 0.14));

	if (pattern == CONCRETE) {
		h = fine * 0.0015 + fbm3(wp * 3.0) * 0.002;
		col = c * (0.82 + 0.3 * large) * (0.93 + 0.14 * fine);
		float stain = smoothstep(0.55, 0.75, fbm3(wp * 0.6 + 4.0));
		col = mix(col, col * vec3(0.76, 0.74, 0.70), stain * 0.6);
		float pit = step(0.985, hash12(floor(m * 60.0))) * detail_fade(px, 0.017);
		col *= 1.0 - pit * 0.35;
		float crack = crack_network(m, 2.6, 0.006, px, 31.0) * smoothstep(0.5, 0.7, noise2(m * 0.07 + 6.0));
		col *= 1.0 - crack * 0.5;
		h -= pit * 0.002 + crack * 0.002;
	} else if (pattern == BLOCK) {
		vec2 b = masonry(m, vec2(0.40, 0.20), 0.012, px);
		vec3 block = c * (0.86 + 0.22 * b.y) * (0.9 + 0.18 * fine) * (0.9 + 0.2 * large);
		col = mix(block, c * 0.72, b.x);
		h = (1.0 - b.x) * 0.004 + fine * 0.001;
		rough = mix(roughness_base, 0.97, b.x);
	} else if (pattern == BRICK) {
		float mortar;
		col = brick_color(m, fine, px, mortar) * (0.86 + 0.28 * large);
		h = (1.0 - mortar) * 0.004 + fine * 0.001;
		rough = mix(roughness_base, 0.97, mortar);
	} else if (pattern == PLASTER) {
		// Patches of render have fallen off, showing the brick behind. The coat is ~1 cm thick: its
		// broken edge is lighter, and the brick right next to it sits in its shadow.
		float f = fbm2(m * 0.9 + 3.7) + (noise2(m * 22.0) - 0.5) * 0.012 * detail_fade(px, 0.04);
		float peel = smoothstep(0.585, 0.588, f);
		float chip = smoothstep(0.578, 0.585, f) * (1.0 - peel);
		float lip = peel * (1.0 - smoothstep(0.588, 0.6, f));
		float mortar;
		vec3 under = brick_color(m, fine, px, mortar) * vec3(0.85, 0.8, 0.78);
		vec3 plaster = c * (0.86 + 0.18 * fbm3(wp * 1.6)) * (0.96 + 0.08 * fine);
		col = mix(plaster, under, peel);
		col = mix(col, plaster * 1.15, chip * 0.7);
		col *= 1.0 - lip * 0.35;
		h = (1.0 - peel) * 0.004 + fine * 0.0008 - peel * mortar * 0.002;
		rough = mix(roughness_base, 0.95, peel);
	} else if (pattern == CORRUGATED) {
		// Sheets ~0.9 m wide: each weathered differently, some patched with newer metal.
		float sheet = hash12(vec2(floor(m.x / 0.9), 7.0));
		float ridge = sin(m.x * 6.2831853 / 0.076) * (1.0 - smoothstep(0.15, 0.4, fwidth(m.x) / 0.076));
		h = ridge * 0.009;
		col = c * (0.82 + 0.3 * sheet) * (0.92 + 0.08 * ridge) * (0.9 + 0.2 * large);
		float streak = smoothstep(0.45, 0.85, fbm2(vec2(m.x * 3.0, m.y * 0.22) + 9.1) + (sheet - 0.5) * 0.3);
		float rust = streak * (0.35 + weathering * 0.65);
		col = mix(col, rust_col * (0.8 + 0.4 * fine), rust * 0.8);
		rough = mix(roughness_base, 0.92, rust);
		metal = mix(metallic_base, 0.0, rust);
	} else if (pattern == METAL) {
		float scratch = smoothstep(0.93, 1.0, noise2(vec2(m.x * 40.0, m.y * 2.0))) * detail_fade(px, 0.025);
		col = c * (0.9 + 0.12 * large);
		col = mix(col, srgb(vec3(0.62)), scratch * 0.4);
		float rust = smoothstep(0.62, 0.8, fbm3(wp * 1.3)) * weathering;
		col = mix(col, rust_col, rust);
		rough = mix(roughness_base, 0.88, rust);
		metal = mix(metallic_base, 0.0, rust);
		h = fine * 0.0005 + rust * 0.0015;
	} else if (pattern == RUST) {
		float r = fbm3(wp * 2.5);
		col = mix(c, rust_col, smoothstep(0.3, 0.7, r)) * (0.82 + 0.3 * fine);
		h = r * 0.002 + fine * 0.001;
		rough = mix(roughness_base, 0.92, r);
		metal = metallic_base * (1.0 - r);
	} else if (pattern == ASPHALT) {
		float grain = mix(0.5, hash12(floor(m * 220.0)), detail_fade(px, 0.0045));
		float aggregate = mix(0.5, noise2(m * 31.0), detail_fade(px, 0.03));
		col = c * (0.84 + 0.32 * grain) * (0.9 + 0.2 * aggregate) * (0.86 + 0.28 * large);
		float worn = smoothstep(0.42, 0.62, noise2(m * 0.045 + 2.0));
		float crack = 0.0;
		if (worn > 0.0) {
			crack = crack_network(m, 3.2, 0.012, px, 3.0) * worn;
		}
		col = mix(col, col * 0.4, crack * 0.85);
		float repair = smoothstep(0.66, 0.69, noise2(m * 0.06 + 11.0));
		col = mix(col, col * 0.8, repair);
		float oil = smoothstep(0.7, 0.8, fbm2(m * 0.35 + 21.0)) * smoothstep(0.5, 0.7, noise2(m * 0.08 + 5.0));
		col = mix(col, col * 0.62, oil * 0.6);
		rough = mix(roughness_base, 0.7, oil);
		h = -crack * 0.003 + grain * 0.0008 + aggregate * 0.0012;
	} else if (pattern == DIRT) {
		col = c * (0.78 + 0.38 * fbm3(wp * 0.8)) * (0.9 + 0.2 * fine);
		vec3 w = worley(m * 9.0);
		float pebble = mix(0.05, 1.0 - smoothstep(0.08, 0.16, w.x), detail_fade(px, 0.026));
		col = mix(col, c * (1.0 + 0.5 * hash12(w.yz)), pebble * 0.6);
		h = pebble * 0.004 + fine * 0.002;
		rough = 0.98;
	} else if (pattern == GRAVEL) {
		vec3 w = worley(m * 16.0);
		float sharp = detail_fade(px, 0.03);
		float stone = mix(0.35, 1.0 - smoothstep(0.22, 0.42, w.x), sharp);
		col = mix(c * 0.55, c * (0.75 + 0.6 * mix(0.5, hash12(w.yz), sharp)), stone);
		h = stone * 0.007;
		rough = 0.95;
	} else if (pattern == WOOD) {
		float board = floor(m.y / 0.12);
		float f = fract(m.y / 0.12);
		float gap = mix(0.06, 1.0 - smoothstep(0.0, 0.06, min(f, 1.0 - f)), 1.0 - smoothstep(0.02, 0.048, px));
		float grain = mix(0.5, noise2(vec2(m.x * 3.0 + board * 5.0, m.y * 60.0)), detail_fade(px, 0.017));
		col = c * (0.78 + 0.32 * hash12(vec2(board, 3.0))) * (0.84 + 0.26 * grain);
		col = mix(col, c * 0.32, gap);
		col = mix(col, col * vec3(0.7, 0.72, 0.72), smoothstep(0.5, 0.8, large) * weathering); // grey, sun-bleached
		h = (1.0 - gap) * 0.003 + grain * 0.0006;
	} else if (pattern == TARP) {
		float weave = sin(m.x * 400.0) * sin(m.y * 400.0) * detail_fade(px, 0.008);
		col = c * (0.86 + 0.06 * weave + 0.2 * large);
		h = weave * 0.0003 + fbm3(wp * 4.0) * 0.004;
	} else if (pattern == RUBBER) {
		col = c * (0.88 + 0.24 * fine);
		h = fine * 0.0008;
	} else if (pattern == GRASS) {
		// Dry scrub: green and straw patches at two scales, so it doesn't tile in the distance.
		float patchy = fbm2(m * 0.6) * 0.6 + fbm2(m * 0.03 + 40.0) * 0.8 - 0.2;
		float blade = mix(0.5, hash12(floor(m * 90.0)), detail_fade(px, 0.011));
		col = mix(c, srgb(vec3(0.52, 0.48, 0.32)), smoothstep(0.3, 0.75, patchy)) * (0.82 + 0.34 * blade);
		col = mix(col, c * 0.6, smoothstep(0.55, 0.8, fbm2(m * 0.12 + 9.0)) * 0.5); // darker clumps
		h = blade * 0.003;
		rough = 0.98;
	} else {
		col = c * (0.92 + 0.12 * large);
	}
}

// Bump mapping without tangents (Mikkelsen 2010): perturbs the view-space normal by the screen-space
// gradient of a height field given in metres.
vec3 perturb_normal(vec3 n, vec3 pos, float h) {
	vec3 dpdx = dFdx(pos);
	vec3 dpdy = dFdy(pos);
	float dhdx = dFdx(h);
	float dhdy = dFdy(h);
	vec3 r1 = cross(dpdy, n);
	vec3 r2 = cross(n, dpdx);
	float det = dot(dpdx, r1);
	vec3 grad = sign(det) * (dhdx * r1 + dhdy * r2);
	return normalize(abs(det) * n - grad);
}

void fragment() {
	vec2 m = UV;
	float px = length(fwidth(m));
	vec3 col;
	float rough;
	float metal;
	float h;
	surface_pattern(m, world_pos, px, col, rough, metal, h);

	vec2 tuv = m / tile_m;
	if (use_textures) {
		col = texture(albedo_tex, tuv).rgb;
		// Broad light and dark patches tens of metres across, so a photo repeating every couple of
		// metres doesn't read as a grid over a yard or a long wall.
		col *= mix(0.84, 1.12, fbm2(world_pos.xz * 0.045 + vec2(world_pos.y * 0.03)));
		h = 0.0;
		if (has_roughness_tex) {
			rough = texture(roughness_tex, tuv).r;
		}
	}

	// Weathering: grime at the foot of walls, moss where they meet the ground outside, rain
	// streaks from their tops, dust on ledges. Pieces that start well above the ground (lintels,
	// upper walls, slab edges) get only a little grime and no moss.
	vec3 wn = world_normal;
	float above = UV2.x;
	float base_y = world_pos.y - above;
	float grounded = 1.0 - smoothstep(0.1, 0.5, base_y);
	float vertical = 1.0 - abs(wn.y);
	float grime = weathering * vertical * (1.0 - smoothstep(0.0, 0.9 + 0.8 * fbm3(world_pos * 0.7), above));
	grime *= mix(0.3, 1.0, grounded);
	col = mix(col, col * vec3(0.55, 0.53, 0.47), grime * 0.85);
	float moss = weathering * vertical * grounded * (1.0 - smoothstep(0.0, 0.35, above)) * smoothstep(0.5, 0.7, fbm3(world_pos * 2.0 + 7.0));
	col = mix(col, srgb(vec3(0.27, 0.31, 0.18)), moss * 0.6);
	float to_top = UV2.y - above;
	float streak = weathering * vertical * smoothstep(0.5, 0.9, fbm2(vec2(m.x * 2.0, m.y * 0.1)));
	streak *= 0.5 + 0.5 * (1.0 - smoothstep(0.0, 3.0, to_top));
	col *= 1.0 - streak * 0.28;
	rough = mix(rough, 1.0, grime * 0.5);
	float ledge = smoothstep(0.7, 0.95, wn.y) * step(0.05, world_pos.y);
	col = mix(col, col * 1.08 + srgb(vec3(0.12)), ledge * weathering * 0.3);

	// Fade the procedural bump with distance so far surfaces don't shimmer.
	h *= 1.0 - smoothstep(18.0, 45.0, length(VERTEX));
	NORMAL = perturb_normal(NORMAL, VERTEX, h);
	if (use_textures && has_normal_tex) {
		NORMAL_MAP = texture(normal_tex, tuv).rgb;
	} else {
		NORMAL_MAP = vec3(0.5, 0.5, 1.0);
	}

	ALBEDO = clamp(col, vec3(0.0), vec3(1.0));
	ROUGHNESS = clamp(rough, 0.04, 1.0);
	METALLIC = clamp(metal, 0.0, 1.0);
}
