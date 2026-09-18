shader_type canvas_item;

// 도트 스프라이트의 특정 색만 다른 색으로 바꾼다 (팔레트 스왑).
//
// 왜 이게 되는가: 이 그림은 색이 14개뿐이고 안티앨리어싱이 없다. 즉 털에 쓰인 색이
// 딱 그 값으로만 존재하므로, **정확히 일치하는 색만** 갈아끼우면 경계가 뭉개지지 않는다.
// 외곽선·눈·귀 안쪽은 표에 넣지 않아 그대로 남는다.
//
// 텍스처 필터는 nearest 라 보간된 중간색이 생기지 않는다. 그래도 파일 인코딩 오차를
// 감안해 아주 작은 허용치를 둔다.

const int MAX_PAIRS = 16;

uniform int pair_count = 0;
uniform vec3 src_colors[MAX_PAIRS];
uniform vec3 dst_colors[MAX_PAIRS];
uniform float tolerance : hint_range(0.0, 0.2) = 0.02;

void fragment() {
	vec4 tex = texture(TEXTURE, UV);
	vec3 rgb = tex.rgb;

	for (int i = 0; i < pair_count; i++) {
		if (distance(rgb, src_colors[i]) < tolerance) {
			rgb = dst_colors[i];
			break;
		}
	}

	// 들어온 COLOR 는 modulate(고스트 반투명 등) — 곱해서 유지한다.
	COLOR = vec4(rgb, tex.a) * COLOR;
}
