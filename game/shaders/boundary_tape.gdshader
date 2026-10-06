// The tape round a place (PlaceBoundary) and the posts it's strung on. The tape is a ribbon (UV.x the
// distance along it in metres, UV.y 0 at its bottom edge to 1 at its top; COLOR.a its slack, 0 where
// it's held to 1 mid-span) striped on the slant in two colours, fluttering across itself in the wind
// where it's slack. The posts (posts = true) are banded in the same colours by height.
shader_type spatial;
render_mode cull_disabled, diffuse_burley, specular_schlick_ggx, world_vertex_coords;

uniform vec3 color_a : source_color = vec3(0.77, 0.18, 0.13);
uniform vec3 color_b : source_color = vec3(0.93, 0.91, 0.88);
// The length of one stripe along the tape (m), the tape's width (m) and the posts' band length (m).
uniform float stripe = 0.11;
uniform float width = 0.06;
uniform float band = 0.2;
// How far a slack span moves across itself (m); the wind it moves in (world, horizontal, normalised).
uniform float flutter = 0.025;
uniform vec3 wind = vec3(1.0, 0.0, 0.0);
uniform bool posts = false;

varying float stripes;

void vertex() {
	if (posts) {
		stripes = VERTEX.y / band;
	} else {
		stripes = (UV.x + UV.y * width) / stripe;
		float slack = COLOR.a;
		// A gust rolls along the tape; the wind pushes it more when it's broadside on.
		float push = 0.4 + 0.6 * abs(dot(NORMAL, wind));
		float wave = sin(TIME * 5.3 + UV.x * 1.9) + 0.5 * sin(TIME * 8.1 - UV.x * 3.7);
		VERTEX += NORMAL * (flutter * slack * push * wave);
	}
}

void fragment() {
	ALBEDO = fract(stripes) < 0.5 ? color_a : color_b;
	ROUGHNESS = posts ? 0.6 : 0.45;
	SPECULAR = 0.4;
}
