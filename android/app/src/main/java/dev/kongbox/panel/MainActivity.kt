package dev.kongbox.panel

import android.annotation.SuppressLint
import android.app.Activity
import android.content.Context
import android.content.Intent
import android.content.IntentFilter
import android.content.pm.ActivityInfo
import android.graphics.Color
import android.os.BatteryManager
import android.os.Build
import android.os.Bundle
import android.os.Handler
import android.os.Looper
import android.os.VibrationEffect
import android.os.Vibrator
import android.text.format.DateFormat
import android.view.HapticFeedbackConstants
import android.view.KeyEvent
import android.view.View
import android.view.WindowInsets
import android.view.WindowInsetsController
import android.view.WindowManager
import android.webkit.ConsoleMessage
import android.webkit.JavascriptInterface
import android.webkit.WebChromeClient
import android.webkit.WebResourceError
import android.webkit.WebResourceRequest
import android.webkit.WebView
import android.webkit.WebViewClient
import org.json.JSONObject
import java.net.HttpURLConnection
import java.net.URL
import java.security.SecureRandom
import kotlin.concurrent.thread

/**
 * Full-screen WebView showing the agent's panel at http://127.0.0.1:8765/panel/ (adb reverse makes that the PC).
 * When the agent is unreachable a bundled offline screen shows and the app retries every 3 seconds.
 */
class MainActivity : Activity() {
    companion object {
        const val PANEL = "http://127.0.0.1:8765/panel/"
        const val PROBE = "http://127.0.0.1:8765/api/info"
        const val OFFLINE = "file:///android_asset/offline.html"
    }

    private lateinit var web: WebView
    private val main = Handler(Looper.getMainLooper())
    private val bridgeKey = ByteArray(16).also { SecureRandom().nextBytes(it) }.joinToString("") { "%02x".format(it) }
    @Volatile private var volumeKeysFlipPages = true
    @Volatile private var offline = false
    private var probing = false

    @SuppressLint("SetJavaScriptEnabled", "AddJavascriptInterface")
    override fun onCreate(savedInstanceState: Bundle?) {
        super.onCreate(savedInstanceState)
        window.addFlags(WindowManager.LayoutParams.FLAG_KEEP_SCREEN_ON)
        window.decorView.setBackgroundColor(Color.BLACK)
        if (Build.VERSION.SDK_INT >= 28) {
            window.attributes = window.attributes.apply { layoutInDisplayCutoutMode = WindowManager.LayoutParams.LAYOUT_IN_DISPLAY_CUTOUT_MODE_SHORT_EDGES }
        }

        web = WebView(this)
        web.setBackgroundColor(Color.BLACK)
        web.isVerticalScrollBarEnabled = false
        web.isHorizontalScrollBarEnabled = false
        web.overScrollMode = View.OVER_SCROLL_NEVER
        with(web.settings) {
            javaScriptEnabled = true
            domStorageEnabled = true
            mediaPlaybackRequiresUserGesture = false
            setSupportZoom(false)
            builtInZoomControls = false
            displayZoomControls = false
            allowFileAccess = true          // only for the bundled offline screen
            allowContentAccess = false
            @Suppress("DEPRECATION")
            allowFileAccessFromFileURLs = false
            @Suppress("DEPRECATION")
            allowUniversalAccessFromFileURLs = false
        }
        web.webChromeClient = object : WebChromeClient() {
            override fun onConsoleMessage(message: ConsoleMessage): Boolean = true // keep logcat quiet
        }
        web.webViewClient = object : WebViewClient() {
            override fun shouldOverrideUrlLoading(view: WebView, request: WebResourceRequest): Boolean {
                // the panel never navigates away; anything else stays out of the kiosk
                val url = request.url.toString()
                return !(url.startsWith(PANEL) || url.startsWith(OFFLINE))
            }

            override fun onReceivedError(view: WebView, request: WebResourceRequest, error: WebResourceError) {
                if (request.isForMainFrame) goOffline()
            }

            override fun onPageFinished(view: WebView, url: String?) {
                if (url != null && url.startsWith(PANEL)) giveKey(view)
            }
        }
        web.addJavascriptInterface(Bridge(), "Android")
        setContentView(web)
        hideSystemUi()
        web.loadUrl(PANEL)
    }

    /** The key reaches the main frame only, so a sandboxed custom-HTML widget cannot drive the bridge. */
    private fun giveKey(view: WebView) {
        view.evaluateJavascript("window.KB && (KB.key='$bridgeKey', KB.onKey && KB.onKey());", null)
    }

    private fun goOffline() {
        if (offline) return
        offline = true
        web.loadUrl(OFFLINE)
        probe()
    }

    private fun probe() {
        if (probing) return
        probing = true
        thread(isDaemon = true, name = "probe") {
            while (offline) {
                val ok = try {
                    val c = URL(PROBE).openConnection() as HttpURLConnection
                    c.connectTimeout = 1500
                    c.readTimeout = 1500
                    val code = c.responseCode
                    c.disconnect()
                    code == 200
                } catch (_: Exception) { false }
                if (ok) {
                    offline = false
                    main.post { web.loadUrl(PANEL) }
                    break
                }
                Thread.sleep(3000)
            }
            probing = false
        }
    }

    override fun onWindowFocusChanged(hasFocus: Boolean) {
        super.onWindowFocusChanged(hasFocus)
        if (hasFocus) hideSystemUi()
    }

    @Suppress("DEPRECATION")
    private fun hideSystemUi() {
        if (Build.VERSION.SDK_INT >= 30) {
            window.setDecorFitsSystemWindows(false)
            window.insetsController?.let {
                it.hide(WindowInsets.Type.systemBars())
                it.systemBarsBehavior = WindowInsetsController.BEHAVIOR_SHOW_TRANSIENT_BARS_BY_SWIPE
            }
        } else {
            window.decorView.systemUiVisibility = (View.SYSTEM_UI_FLAG_IMMERSIVE_STICKY or View.SYSTEM_UI_FLAG_FULLSCREEN
                or View.SYSTEM_UI_FLAG_HIDE_NAVIGATION or View.SYSTEM_UI_FLAG_LAYOUT_STABLE
                or View.SYSTEM_UI_FLAG_LAYOUT_HIDE_NAVIGATION or View.SYSTEM_UI_FLAG_LAYOUT_FULLSCREEN)
        }
    }

    override fun dispatchKeyEvent(event: KeyEvent): Boolean {
        if (volumeKeysFlipPages && !offline && (event.keyCode == KeyEvent.KEYCODE_VOLUME_UP || event.keyCode == KeyEvent.KEYCODE_VOLUME_DOWN)) {
            if (event.action == KeyEvent.ACTION_DOWN && event.repeatCount == 0) {
                val dir = if (event.keyCode == KeyEvent.KEYCODE_VOLUME_UP) -1 else 1
                web.evaluateJavascript("window.KB && KB.flip && KB.flip($dir);", null)
            }
            return true
        }
        return super.dispatchKeyEvent(event)
    }

    @Deprecated("Kiosk: back does nothing")
    override fun onBackPressed() { /* the panel is the whole app */ }

    override fun onResume() {
        super.onResume()
        web.onResume()
        hideSystemUi()
    }

    override fun onPause() {
        web.onPause()
        super.onPause()
    }

    override fun onDestroy() {
        offline = false
        web.destroy()
        super.onDestroy()
    }

    /** window.Android — every call carries the key handed to the main frame. */
    inner class Bridge {
        private fun ok(key: String) = key == bridgeKey

        @JavascriptInterface
        fun haptic(key: String) {
            if (ok(key)) main.post { web.performHapticFeedback(HapticFeedbackConstants.KEYBOARD_TAP) }
        }

        @JavascriptInterface
        fun buzz(key: String, pattern: String) {
            if (!ok(key)) return
            val p = pattern.split(',').mapNotNull { it.trim().toLongOrNull() }.take(12).map { it.coerceIn(0, 1500) }.toLongArray()
            if (p.isEmpty()) return
            val v = getSystemService(Context.VIBRATOR_SERVICE) as? Vibrator ?: return
            if (Build.VERSION.SDK_INT >= 26) v.vibrate(VibrationEffect.createWaveform(p, -1))
            else @Suppress("DEPRECATION") v.vibrate(p, -1)
        }

        /** 0…1, or -1 to hand brightness back to the system. */
        @JavascriptInterface
        fun setBrightness(key: String, value: Float) {
            if (!ok(key)) return
            main.post {
                window.attributes = window.attributes.apply {
                    screenBrightness = if (value < 0) WindowManager.LayoutParams.BRIGHTNESS_OVERRIDE_NONE else value.coerceIn(0.01f, 1f)
                }
            }
        }

        @JavascriptInterface
        fun battery(key: String): String {
            if (!ok(key)) return "{}"
            val i: Intent = registerReceiver(null, IntentFilter(Intent.ACTION_BATTERY_CHANGED)) ?: return "{}"
            val level = i.getIntExtra(BatteryManager.EXTRA_LEVEL, -1)
            val scale = i.getIntExtra(BatteryManager.EXTRA_SCALE, 100).coerceAtLeast(1)
            val status = i.getIntExtra(BatteryManager.EXTRA_STATUS, -1)
            val temp = i.getIntExtra(BatteryManager.EXTRA_TEMPERATURE, 0) / 10.0
            return JSONObject()
                .put("level", if (level < 0) JSONObject.NULL else level * 100 / scale)
                .put("charging", status == BatteryManager.BATTERY_STATUS_CHARGING || status == BatteryManager.BATTERY_STATUS_FULL)
                .put("temp", temp)
                .toString()
        }

        @JavascriptInterface
        fun setVolumeKeys(key: String, enabled: Boolean) { if (ok(key)) volumeKeysFlipPages = enabled }

        @JavascriptInterface
        fun is24Hour(key: String): Boolean = if (ok(key)) DateFormat.is24HourFormat(this@MainActivity) else true

        @JavascriptInterface
        fun setOrientation(key: String, mode: String) {
            if (!ok(key)) return
            main.post {
                requestedOrientation = when (mode) {
                    "landscape" -> ActivityInfo.SCREEN_ORIENTATION_SENSOR_LANDSCAPE
                    "portrait" -> ActivityInfo.SCREEN_ORIENTATION_SENSOR_PORTRAIT
                    else -> ActivityInfo.SCREEN_ORIENTATION_FULL_USER
                }
            }
        }

        @JavascriptInterface
        fun version(key: String): String = if (ok(key)) BuildConfig.VERSION_NAME else ""
    }
}
