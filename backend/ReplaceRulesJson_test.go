package main

import (
	"encoding/json"
	"testing"
)

func TestApplyJSONBodyRewriteSetsNestedValues(t *testing.T) {
	body := []byte(`{"Ret":200,"Data":{"User":{"IsInsider":false},"Membership":{"Role":1}}}`)

	rewritten, changed := applyJSONBodyRewrite(body, "设置", "Data.User.IsInsider", "true")
	if !changed {
		t.Fatalf("first JSON rewrite should report changed")
	}

	rewritten, changed = applyJSONBodyRewrite(rewritten, "设置", "Data.Membership.Role", "7")
	if !changed {
		t.Fatalf("second JSON rewrite should report changed")
	}

	var decoded map[string]any
	if err := json.Unmarshal(rewritten, &decoded); err != nil {
		t.Fatalf("rewritten body should remain valid JSON: %v\n%s", err, string(rewritten))
	}

	data := decoded["Data"].(map[string]any)
	user := data["User"].(map[string]any)
	if user["IsInsider"] != true {
		t.Fatalf("Data.User.IsInsider = %#v, want true", user["IsInsider"])
	}

	membership := data["Membership"].(map[string]any)
	if membership["Role"] != float64(7) {
		t.Fatalf("Data.Membership.Role = %#v, want 7", membership["Role"])
	}
}

func TestApplyJSONBodyRewriteDeletesNestedValue(t *testing.T) {
	body := []byte(`{"Data":{"User":{"Msg":"expired","IsInsider":false}}}`)

	rewritten, changed := applyJSONBodyRewrite(body, "删除", "Data.User.Msg", "")
	if !changed {
		t.Fatalf("JSON delete should report changed")
	}

	var decoded map[string]any
	if err := json.Unmarshal(rewritten, &decoded); err != nil {
		t.Fatalf("rewritten body should remain valid JSON: %v\n%s", err, string(rewritten))
	}

	user := decoded["Data"].(map[string]any)["User"].(map[string]any)
	if _, exists := user["Msg"]; exists {
		t.Fatalf("Data.User.Msg should be deleted")
	}
	if user["IsInsider"] != false {
		t.Fatalf("unrelated JSON value should be preserved")
	}
}
