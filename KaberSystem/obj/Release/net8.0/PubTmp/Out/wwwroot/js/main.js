window.onload = function () {
    window.scrollTo(0, 0);
};

document.addEventListener('DOMContentLoaded', function () {
    'use strict';

    // 1. Navbar Scroll Effect
    const navbar = document.querySelector(".navbar");
    if (navbar) {
        window.addEventListener("scroll", function () {
            if (window.scrollY > 50) {
                navbar.classList.add("scrolled");
            } else {
                navbar.classList.remove("scrolled");
            }
        });
    }

    // 2. Navbar Collapse Background
    const navbarCollapse = document.getElementById('navbarSupportedContent');
    if (navbarCollapse) {
        navbarCollapse.addEventListener("show.bs.collapse", function () {
            if (navbar) navbar.classList.add("show-bg");
        });
        navbarCollapse.addEventListener("hide.bs.collapse", function () {
            if (navbar) navbar.classList.remove("show-bg");
        });

        const navLinks = document.querySelectorAll('.navbar-nav .nav-link');
        navLinks.forEach(function (link) {
            link.addEventListener('click', function () {
                const isNavbarVisible = window.getComputedStyle(navbarCollapse).display !== 'none';
                if (isNavbarVisible && window.innerWidth < 992) {
                    // Check if bootstrap is defined to avoid errors
                    if (typeof bootstrap !== 'undefined') {
                        const collapseInstance = bootstrap.Collapse.getInstance(navbarCollapse);
                        if (collapseInstance) {
                            collapseInstance.hide();
                        }
                    }
                }
            });
        });
    }

    // 3. Counters
    const counters = document.querySelectorAll(".counter");
    counters.forEach(function (counter) {
        counter.innerText = "0";
        const updateCounter = function () {
            const target = +counter.getAttribute("data-target");
            const c = +counter.innerText;
            const increment = target / 200;

            if (c < target) {
                counter.innerText = Math.ceil(c + increment).toString();
                setTimeout(updateCounter, 10);
            } else {
                counter.innerText = target.toString();
            }
        };
        updateCounter();
    });

    // 4. Swiper Slider
    const swiperElement = document.querySelector('.testimonialSwiper');
    if (swiperElement && typeof Swiper !== 'undefined') {
        new Swiper(".testimonialSwiper", {
            slidesPerView: 3,
            spaceBetween: 30,
            loop: true,
            autoplay: {
                delay: 2500,
                disableOnInteraction: false,
                pauseOnMouseEnter: true
            },
            grabCursor: true,
            simulateTouch: true,
            speed: 900,
            breakpoints: {
                0: { slidesPerView: 1 },
                576: { slidesPerView: 1 },
                768: { slidesPerView: 2 },
                992: { slidesPerView: 2 },
                1200: { slidesPerView: 3 }
            }
        });
    }

    // 5. FAQ Accordion
    const faqs = document.querySelectorAll(".faq-item");
    faqs.forEach(function (item) {
        const question = item.querySelector(".faq-question");
        if (question) {
            question.addEventListener("click", function () {
                item.classList.toggle("active");
            });
        }
    });

    // 6. Back To Top Button
    const backToTop = document.getElementById("backToTop");
    if (backToTop) {
        window.addEventListener("scroll", function () {
            if (window.scrollY > 300) {
                backToTop.style.display = "flex";
            } else {
                backToTop.style.display = "none";
            }
        });

        backToTop.addEventListener("click", function () {
            window.scrollTo({
                top: 0,
                behavior: "smooth"
            });
        });
    }

    // 7. Contact Form Validation and Submission
    const contactForm = document.getElementById("contactForm");
    if (contactForm) {
        const name = document.getElementById("name");
        const email = document.getElementById("email");
        const phone = document.getElementById("phone");
        const address = document.getElementById("address");
        const message = document.getElementById("message");

        const emailRegex = /^[^\s@]+@[^\s@]+\.[^\s@]+$/;
        const phoneRegex = /^[0-9]{9,15}$/;

        function validateInput(input, type = "text") {
            if (!input) return false;
            const error = input.nextElementSibling;
            input.classList.remove("valid", "invalid");

            if (input.value.trim() === "") {
                if (error) error.textContent = "هذا الحقل مطلوب";
                input.classList.add("invalid");
                return false;
            }

            if (type === "email" && !emailRegex.test(input.value)) {
                if (error) error.textContent = "البريد الإلكتروني غير صحيح";
                input.classList.add("invalid");
                return false;
            }

            if (type === "phone" && !phoneRegex.test(input.value)) {
                if (error) error.textContent = "رقم الهاتف غير صحيح";
                input.classList.add("invalid");
                return false;
            }

            if (error) error.textContent = "";
            input.classList.add("valid");
            return true;
        }

        if (name) name.addEventListener("input", function () { validateInput(name); });
        if (address) address.addEventListener("input", function () { validateInput(address); });
        if (message) message.addEventListener("input", function () { validateInput(message); });
        if (email) email.addEventListener("input", function () { validateInput(email, "email"); });
        if (phone) phone.addEventListener("input", function () { validateInput(phone, "phone"); });

        contactForm.addEventListener("submit", function (e) {
            e.preventDefault();
            let isValid = true;

            if (!validateInput(name)) isValid = false;
            if (!validateInput(email, "email")) isValid = false;
            if (!validateInput(phone, "phone")) isValid = false;
            if (!validateInput(address)) isValid = false;
            if (!validateInput(message)) isValid = false;

            if (isValid && typeof Swal !== 'undefined') {
                Swal.fire({
                    icon: "success",
                    title: "تم الإرسال",
                    text: "تم إرسال رسالتك بنجاح",
                    confirmButtonText: "حسناً"
                }).then(function () {
                    let whatsappMessage = "الاسم: " + name.value + "\n" +
                        "البريد الإلكتروني: " + email.value + "\n" +
                        "رقم الهاتف: " + phone.value + "\n" +
                        "العنوان: " + address.value + "\n" +
                        "الرسالة: " + message.value;

                    let encodedMessage = encodeURIComponent(whatsappMessage);
                    window.open("https://wa.me/966541683466?text=" + encodedMessage, "_blank");

                    contactForm.reset();
                    [name, email, phone, address, message].forEach(function (input) {
                        if (input) {
                            input.classList.remove("valid", "invalid");
                            if (input.nextElementSibling) {
                                input.nextElementSibling.textContent = "";
                            }
                        }
                    });
                });
            }
        });
    }

    // 8. Initialize AOS Animation Library
    if (typeof AOS !== 'undefined') {
        AOS.init({ offset: 120, duration: 1000, easing: 'ease-in-out' });
    }

});