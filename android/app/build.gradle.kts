plugins {
    id("com.android.application")
    id("org.jetbrains.kotlin.android")
}

android {
    namespace = "com.kyaaadood.transferdood"
    compileSdk = 35

    defaultConfig {
        applicationId = "com.kyaaadood.transferdood"
        minSdk = 24
        targetSdk = 34
        versionCode = 1
        versionName = "0.1.0"
    }

    // Firma propia opcional (para publicar en GitHub): si defines TD_KEYSTORE, TD_KEYSTORE_PASSWORD, TD_KEY_ALIAS y
    // TD_KEY_PASSWORD como variables de entorno, el APK release se firma con esa llave (así las actualizaciones encajan).
    val tdKeystore = System.getenv("TD_KEYSTORE")
    if (!tdKeystore.isNullOrEmpty()) {
        signingConfigs {
            create("publish") {
                storeFile = file(tdKeystore)
                storePassword = System.getenv("TD_KEYSTORE_PASSWORD")
                keyAlias = System.getenv("TD_KEY_ALIAS")
                keyPassword = System.getenv("TD_KEY_PASSWORD")
            }
        }
    }

    buildTypes {
        release {
            // Sin dependencias externas: R8 deja el APK diminuto.
            isMinifyEnabled = true
            isShrinkResources = true
            proguardFiles(getDefaultProguardFile("proguard-android-optimize.txt"), "proguard-rules.pro")
            // Sin llave propia se firma con la de depuración, para que el APK se pueda instalar directo (uso personal).
            signingConfig = if (!tdKeystore.isNullOrEmpty()) signingConfigs.getByName("publish") else signingConfigs.getByName("debug")
        }
    }

    compileOptions {
        sourceCompatibility = JavaVersion.VERSION_17
        targetCompatibility = JavaVersion.VERSION_17
    }

    kotlinOptions {
        jvmTarget = "17"
    }

    lint {
        abortOnError = false
    }
}
