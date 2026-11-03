package com.p1studio.inkturfdemo;

import android.app.Activity;
import android.content.Intent;
import android.content.SharedPreferences;
import android.content.res.ColorStateList;
import android.graphics.Color;
import android.graphics.Paint;
import android.graphics.Typeface;
import android.graphics.drawable.GradientDrawable;
import android.net.Uri;
import android.os.Bundle;
import android.util.TypedValue;
import android.view.Gravity;
import android.view.View;
import android.view.ViewGroup;
import android.view.Window;
import android.view.WindowManager;
import android.widget.CheckBox;
import android.widget.CompoundButton;
import android.widget.FrameLayout;
import android.widget.LinearLayout;
import android.widget.ScrollView;
import android.widget.TextView;

/**
 * 隐私同意启动页（TapTap 隐私合规）。
 *
 * 为什么必须是原生 Activity：
 * Unity3d 框架会在 UnityPlayer 初始化阶段读取传感器列表等系统接口。如果同意弹窗由
 * Unity 内的 C# 绘制，引擎此时已经启动，检测仍会判定「同意前收集个人信息」。
 * 因此把同意页放在原生 Activity 里：用户点击「同意并继续」之前不创建 UnityPlayer，
 * 同意后才显式启动 com.unity3d.player.UnityPlayerActivity。
 *
 * 约束：
 * - POLICY_VERSION 必须与 Assets/_Project/Scripts/UI/PrivacyConsentDialog.cs 的
 *   PolicyVersion 完全一致；tools/build_privacy_activity.sh 会做一致性校验。
 * - 本类不得引用 Unity / 任何第三方 SDK，也不得访问网络、设备标识、传感器或应用列表。
 * - 文案或布局改动后，重新运行 tools/build_privacy_activity.sh 生成 privacy-consent.jar。
 */
public class PrivacyActivity extends Activity {
    public static final String PREFS_NAME = "P1Privacy";
    public static final String KEY_ACCEPTED = "accepted";
    public static final String KEY_VERSION = "version";
    public static final String POLICY_VERSION = "2026-11-02";

    private static final String POLICY_URL = "https://favoroo.github.io/moshuizuozhan/privacy.html";
    private static final String TERMS_URL = "https://favoroo.github.io/moshuizuozhan/terms.html";

    private static final int INK = 0xFF17171A;
    private static final int PAPER = 0xFFF7F2E6;
    private static final int ORANGE = 0xFFE8622A;
    private static final int MUTED = 0xFF6F6A60;
    private static final int BACKDROP = 0xFF141414;

    private boolean launching;

    @Override
    protected void onCreate(Bundle savedInstanceState) {
        super.onCreate(savedInstanceState);
        requestWindowFeature(Window.FEATURE_NO_TITLE);
        getWindow().setFlags(WindowManager.LayoutParams.FLAG_FULLSCREEN,
                WindowManager.LayoutParams.FLAG_FULLSCREEN);
        getWindow().setBackgroundDrawableResource(android.R.color.black);

        SharedPreferences prefs = getSharedPreferences(PREFS_NAME, MODE_PRIVATE);
        if (hasConsent(prefs)) {
            launchGame();
            return;
        }
        setContentView(buildContent(prefs));
    }

    private static boolean hasConsent(SharedPreferences prefs) {
        return prefs.getBoolean(KEY_ACCEPTED, false)
                && POLICY_VERSION.equals(prefs.getString(KEY_VERSION, ""));
    }

    private View buildContent(final SharedPreferences prefs) {
        int screenW = getResources().getDisplayMetrics().widthPixels;
        int screenH = getResources().getDisplayMetrics().heightPixels;
        int cardW = Math.min(screenW - dp(20), dp(820));
        int cardH = Math.min(screenH - dp(20), dp(620));

        FrameLayout root = new FrameLayout(this);
        root.setBackgroundColor(BACKDROP);

        LinearLayout card = new LinearLayout(this);
        card.setOrientation(LinearLayout.HORIZONTAL);
        card.setPadding(dp(24), dp(20), dp(24), dp(20));
        GradientDrawable cardBg = new GradientDrawable();
        cardBg.setColor(PAPER);
        cardBg.setCornerRadius(dp(14));
        cardBg.setStroke(dp(2), INK);
        card.setBackground(cardBg);

        // 左列：标题 + 可滚动正文。右列固定宽度放链接/勾选/按钮，避免横屏下正文被压成一条缝。
        LinearLayout left = new LinearLayout(this);
        left.setOrientation(LinearLayout.VERTICAL);

        TextView title = new TextView(this);
        title.setText("隐私与用户协议");
        title.setTextSize(TypedValue.COMPLEX_UNIT_SP, 26);
        title.setTextColor(INK);
        title.setTypeface(Typeface.DEFAULT_BOLD);
        left.addView(title);

        TextView subtitle = new TextView(this);
        subtitle.setText("墨水大作战 InkTurf · 首次启动需要你的同意");
        subtitle.setTextSize(TypedValue.COMPLEX_UNIT_SP, 13);
        subtitle.setTextColor(MUTED);
        LinearLayout.LayoutParams subLp = new LinearLayout.LayoutParams(
                ViewGroup.LayoutParams.MATCH_PARENT, ViewGroup.LayoutParams.WRAP_CONTENT);
        subLp.topMargin = dp(4);
        left.addView(subtitle, subLp);

        View divider = new View(this);
        divider.setBackgroundColor(ORANGE);
        LinearLayout.LayoutParams divLp = new LinearLayout.LayoutParams(
                ViewGroup.LayoutParams.MATCH_PARENT, dp(3));
        divLp.topMargin = dp(12);
        divLp.bottomMargin = dp(4);
        left.addView(divider, divLp);

        ScrollView scroll = new ScrollView(this);
        scroll.setFillViewport(true);
        TextView body = new TextView(this);
        body.setText(BODY_TEXT);
        body.setTextSize(TypedValue.COMPLEX_UNIT_SP, 14);
        body.setTextColor(INK);
        body.setLineSpacing(dp(3), 1f);
        body.setPadding(0, dp(8), dp(6), dp(8));
        scroll.addView(body, new ScrollView.LayoutParams(
                ViewGroup.LayoutParams.MATCH_PARENT, ViewGroup.LayoutParams.WRAP_CONTENT));
        left.addView(scroll, new LinearLayout.LayoutParams(
                ViewGroup.LayoutParams.MATCH_PARENT, 0, 1f));

        card.addView(left, new LinearLayout.LayoutParams(
                0, ViewGroup.LayoutParams.MATCH_PARENT, 1f));
        card.addView(new View(this), new LinearLayout.LayoutParams(dp(22), 1));

        LinearLayout right = new LinearLayout(this);
        right.setOrientation(LinearLayout.VERTICAL);
        int rightW = Math.max(dp(230), Math.min(dp(310), Math.round(cardW * 0.36f)));
        card.addView(right, new LinearLayout.LayoutParams(
                rightW, ViewGroup.LayoutParams.MATCH_PARENT));

        right.addView(createLink("查看《隐私政策》", POLICY_URL));
        right.addView(createLink("查看《用户协议》", TERMS_URL));

        final CheckBox check = new CheckBox(this);
        check.setText("我已阅读并同意《隐私政策》和《用户协议》");
        check.setTextSize(TypedValue.COMPLEX_UNIT_SP, 14);
        check.setTextColor(INK);
        check.setButtonTintList(ColorStateList.valueOf(ORANGE));
        check.setPadding(0, dp(8), 0, dp(8));
        LinearLayout.LayoutParams checkLp = new LinearLayout.LayoutParams(
                ViewGroup.LayoutParams.MATCH_PARENT, ViewGroup.LayoutParams.WRAP_CONTENT);
        checkLp.topMargin = dp(8);
        right.addView(check, checkLp);

        right.addView(new View(this), new LinearLayout.LayoutParams(
                ViewGroup.LayoutParams.MATCH_PARENT, 0, 1f));

        final TextView agree = createButton("同意并继续", ORANGE, Color.WHITE);
        agree.setEnabled(false);
        agree.setAlpha(0.35f);
        agree.setOnClickListener(new View.OnClickListener() {
            @Override
            public void onClick(View v) {
                if (!check.isChecked()) return;
                prefs.edit()
                        .putBoolean(KEY_ACCEPTED, true)
                        .putString(KEY_VERSION, POLICY_VERSION)
                        .commit();
                launchGame();
            }
        });
        LinearLayout.LayoutParams agreeLp = new LinearLayout.LayoutParams(
                ViewGroup.LayoutParams.MATCH_PARENT, dp(48));
        agreeLp.topMargin = dp(8);
        right.addView(agree, agreeLp);

        TextView decline = new TextView(this);
        decline.setText("不同意并退出");
        decline.setTextSize(TypedValue.COMPLEX_UNIT_SP, 13);
        decline.setTextColor(MUTED);
        decline.setGravity(Gravity.CENTER);
        decline.setPadding(0, dp(10), 0, dp(10));
        decline.setOnClickListener(new View.OnClickListener() {
            @Override
            public void onClick(View v) {
                finishAffinity();
            }
        });
        LinearLayout.LayoutParams declineLp = new LinearLayout.LayoutParams(
                ViewGroup.LayoutParams.MATCH_PARENT, ViewGroup.LayoutParams.WRAP_CONTENT);
        declineLp.topMargin = dp(2);
        right.addView(decline, declineLp);

        check.setOnCheckedChangeListener(new CompoundButton.OnCheckedChangeListener() {
            @Override
            public void onCheckedChanged(CompoundButton buttonView, boolean isChecked) {
                agree.setEnabled(isChecked);
                agree.setAlpha(isChecked ? 1f : 0.35f);
            }
        });

        root.addView(card, new FrameLayout.LayoutParams(cardW, cardH, Gravity.CENTER));
        return root;
    }

    private TextView createLink(final String label, final String url) {
        TextView view = new TextView(this);
        view.setText(label);
        view.setTextSize(TypedValue.COMPLEX_UNIT_SP, 13);
        view.setTextColor(ORANGE);
        view.setPaintFlags(view.getPaintFlags() | Paint.UNDERLINE_TEXT_FLAG);
        view.setGravity(Gravity.CENTER_VERTICAL);
        view.setPadding(0, dp(8), 0, dp(8));
        view.setOnClickListener(new View.OnClickListener() {
            @Override
            public void onClick(View v) {
                try {
                    startActivity(new Intent(Intent.ACTION_VIEW, Uri.parse(url)));
                } catch (Exception ignored) {
                    // 设备没有浏览器时忽略：政策链接不是进入游戏的必经路径。
                }
            }
        });
        return view;
    }

    private TextView createButton(String label, int bgColor, int textColor) {
        TextView view = new TextView(this);
        view.setText(label);
        view.setTextSize(TypedValue.COMPLEX_UNIT_SP, 17);
        view.setTextColor(textColor);
        view.setTypeface(Typeface.DEFAULT_BOLD);
        view.setGravity(Gravity.CENTER);
        GradientDrawable bg = new GradientDrawable();
        bg.setColor(bgColor);
        bg.setCornerRadius(dp(10));
        view.setBackground(bg);
        view.setClickable(true);
        view.setFocusable(true);
        return view;
    }

    /** 同意后才创建 UnityPlayer：这是整条合规链的关键，不能改成 Unity 内弹窗。 */
    private void launchGame() {
        if (launching) return;
        launching = true;
        Intent intent = new Intent();
        intent.setClassName(getPackageName(), "com.unity3d.player.UnityPlayerActivity");
        intent.addFlags(Intent.FLAG_ACTIVITY_NO_ANIMATION);
        startActivity(intent);
        overridePendingTransition(0, 0);
        finish();
    }

    @Override
    public void onBackPressed() {
        // 返回键等同「不同意并退出」，不提供绕过同意进入游戏的路径。
        finishAffinity();
    }

    private int dp(float value) {
        return Math.round(TypedValue.applyDimension(
                TypedValue.COMPLEX_UNIT_DIP, value, getResources().getDisplayMetrics()));
    }

    private static final String BODY_TEXT =
            "《墨水大作战》（InkTurf）由 HQ Games Studio 开发。我们重视你的隐私。\n\n"
            + "在你点击「同意并继续」之前，本游戏不会初始化游戏引擎，也不会收集任何个人信息。\n\n"
            + "同意后，我们仅处理以下必要信息：\n"
            + "· 本地设置：音量、画质、触控布局、隐私同意状态等仅保存在你的设备上，不会上传。\n"
            + "· 检查更新：只有你主动点击「检查更新」时，才会访问 Gitee / GitHub 的公开接口获取版本信息。\n"
            + "· 分发平台：通过 TapTap 下载或启动时，TapTap 按其隐私政策处理相关信息；本游戏未接入 TapTap 登录、广告、统计或支付 SDK。\n\n"
            + "本游戏不提供账号系统，不收集手机号、位置、通讯录、相册、麦克风或摄像头信息，不申请读取应用列表、安装应用等权限。\n\n"
            + "你可以随时在系统设置中清除应用数据或卸载本游戏，删除本地信息并撤回同意。再次启动时会重新询问。";
}
